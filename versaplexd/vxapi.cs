using System;
using System.Linq;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Collections.Generic;
using Wv;
using Wv.Extensions;

internal static class VxDb {
    static WvLog log = new WvLog("VxDb", WvLog.L.Debug2);

    internal static void ExecScalar(string connid, string query, 
			    out VxColumnType coltype, out object result)
    {
        log.print(WvLog.L.Debug3, "ExecScalar {0}\n", query);

	try
	{
	    using (var dbi = VxSqlPool.create(connid))
	    using (var qr = dbi.select(query))
	    {
		var ci = ProcessSchema(qr.columns);
		if (ci.Length == 0)
		{
		    coltype = VxColumnType.String;
		    result = "";
		    return;
		}
		
		coltype = ci[0].VxColumnType;
		var en = qr.GetEnumerator();
		if (en.MoveNext())
		    result = en.Current[0].inner;
		else
		{
		    coltype = VxColumnType.String;
		    result = "";
		}
	    }
        }
	catch (DbException e)
	{
            throw new VxSqlException(e.Message, e);
        }
    }


    internal static void SendChunkRecordSignal(Connection conn,
					       Message call, string sender,
					    VxColumnInfo[] colinfo,
					    object[][] data, byte[][] nulls)
    {
	MessageWriter writer =
	    VxDbInterfaceRouter.PrepareRecordsetWriter(colinfo, data, nulls);
	writer.Write(call.serial);

	Message signal = VxDbus.CreateSignal(sender, "ChunkRecordsetSig",
				   	"a(issnny)vaayu",
					writer);
		    
	// For debugging
	VxDbus.MessageDump(" S>> ", signal);

	conn.Send(signal);
    }


    internal static void ExecChunkRecordset(Connection conn, 
					    Message call, out Message reply)
    {
	string connid = VxDbInterfaceRouter.GetClientId(call);
	
        if (connid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }
        
	var it = call.iter();

        string query = it.pop();
	string iquery = query.ToLower().Trim();
	reply = null;
        // XXX this is fishy, really... whitespace fucks it up.

        if (iquery.StartsWith("list tables"))
            query = "exec sp_tables";
        else if (iquery.StartsWith("list columns "))
            query = String.Format("exec sp_columns @table_name='{0}'",
                    query.Substring(13));
        else if (iquery.StartsWith("list all table") &&
                iquery.StartsWith("list all tablefunction") == false)
            query = "select distinct cast(Name as varchar(max)) Name"
                + " from sysobjects "
                + " where objectproperty(id,'IsTable')=1 "
                + " and xtype='U' "
                + " order by Name ";
        else if (iquery.StartsWith("list all"))
            // Format: list all {view|trigger|procedure|scalarfunction|tablefunction}
            // Returns: a list of all of whatever
            query = String.Format(
                    "select distinct "
                    + " cast (object_name(id) as varchar(256)) Name "
                    + " from syscomments "
                    + " where objectproperty(id,'Is{0}') = 1 "
                    + " order by Name ",
                    query.Split(' ')[2].Trim());
        else if (iquery.StartsWith("get object"))
            // Format: 
            // get object {view|trigger|procedure|scalarfunction|tablefunction} name
            // Returns: the "source code" to the object
            query = String.Format(
                    "select cast(text as varchar(max)) text "
                    + "from syscomments "
                    + "where objectproperty(id, 'Is{0}') = 1 "
                    + "and object_name(id) = '{1}' "
                    + "order by number, colid ",
                    query.Split(' ')[2].Trim(), 
                    query.Split(' ')[3].Trim());
	else if (iquery.StartsWith("drop") || iquery.StartsWith("create") ||
		 iquery.StartsWith("insert") || iquery.StartsWith("update"))
	{
	    //FIXME:  Are the above the only ways to modify a DB, aka the only
	    //        cases where we have to call the old ExecRecordSet?
	    //FIXME:  This is an ugly way to handle these cases, but it works!
	    VxDbInterfaceRouter.CallExecRecordset(conn, call, out reply);
	    return;
	}

        log.print(WvLog.L.Debug3, "ExecChunkRecordset {0}\n", query);

	string sender = call.sender;

        try
	{
	    // FIXME:  Sadly, this is stupidly similar to ExecRecordset.
	    // Anything we can do here to identify commonalities?
            using (var dbi = VxSqlPool.create(connid))
            using (WvSqlRows resultset = dbi.select(query))
	    {
		List<object[]> rows = new List<object[]>();
		List<byte[]> rownulls = new List<byte[]>();
		
		// Our size here is just an approximation.  Start it at 1
		// to ensure that at least one packet always gets sent.
		int cursize = 1;  
		
		var columns = resultset.columns.ToArray();
		VxColumnInfo[] colinfo  = ProcessSchema(columns);
		int ncols = columns.Count();
		
		foreach (WvSqlRow cur_row in resultset)
		{
		    object[] row = new object[ncols];
		    byte[] rownull = new byte[ncols];
		    cursize += rownull.Length;
		    
		    for (int i = 0; i < ncols; i++) 
		    {
			WvAutoCast cval = cur_row[i];
			bool isnull = cval.IsNull;
			
			row[i] = null;
			
			rownull[i] = isnull ? (byte)1 : (byte)0;
			
			switch (colinfo[i].VxColumnType) 
			{
                            case VxColumnType.Int64:
                                row[i] = !isnull ?
                                    (Int64)cval : new Int64();
				cursize += sizeof(Int64);
                                break;
                            case VxColumnType.Int32:
                                row[i] = !isnull ?
                                    (Int32)cval : new Int32();
				cursize += sizeof(Int32);
                                break;
                            case VxColumnType.Int16:
                                row[i] = !isnull ?
                                    (Int16)cval : new Int16();
				cursize += sizeof(Int16);
                                break;
                            case VxColumnType.UInt8:
                                row[i] = !isnull ?
                                    (Byte)cval : new Byte();
				cursize += sizeof(Byte);
                                break;
                            case VxColumnType.Bool:
                                row[i] = !isnull ?
                                    (bool)cval : new Boolean();
				cursize += sizeof(Boolean);
                                break;
                            case VxColumnType.Double:
                                // Might return a Single or Double
                                // FIXME: Check if getting a single causes this
                                // to croak
                                row[i] = !isnull ?
                                    (double)cval : (double)0.0;
				cursize += sizeof(double);
                                break;
                            case VxColumnType.Uuid:
				//FIXME:  Do I work?
                                row[i] = !isnull ?
                                    (string)cval : "";
				cursize += !isnull ?
				    ((string)cval).Length * sizeof(Char) : 0;
                                break;
                            case VxColumnType.Binary:
                                {
                                    if (isnull) 
                                    {
                                        row[i] = new byte[0];
                                        break;
                                    }

				    row[i] = (byte[])cval;
				    cursize += ((byte[])cval).Length;
                                    break;
                                }
                            case VxColumnType.String:
                                row[i] = !isnull ? (string)cval : "";
				cursize += !isnull ?
				    ((string)cval).Length * sizeof(Char) : 0;
                                break;
                            case VxColumnType.DateTime:
                                row[i] = !isnull ?
                                    new VxDbusDateTime((DateTime)cval) :
                                    new VxDbusDateTime();
				cursize += System.Runtime.InteropServices.Marshal.SizeOf((VxDbusDateTime)row[i]);
                                break;
                            case VxColumnType.Decimal:
                                row[i] = !isnull ?
                                    ((Decimal)cval).ToString() : "";
				cursize += ((string)(row[i])).Length *
					    sizeof(Char);
                                break;
			}
		    } // column iterator
		    
		    rows.Add(row);
		    rownulls.Add(rownull);
		    
		    if (cursize >= 1024*1024) //approx 1 MB
		    {
			log.print(WvLog.L.Debug4,
				  "(1 MB reached; {0} rows)\n",
				  rows.Count);
			
			SendChunkRecordSignal(conn, call, sender, colinfo,
					      rows.ToArray(),
					      rownulls.ToArray());
			
			rows = new List<object[]>();
			rownulls = new List<byte[]>();
			cursize = 0;
		    }
		} // row iterator
		
		if (cursize > 0)
		{
		    log.print(WvLog.L.Debug4, "(Remaining data; {0} rows)\n",
			      rows.Count);
		    
		    SendChunkRecordSignal(conn, call, sender, colinfo,
					  rows.ToArray(),
					  rownulls.ToArray());
		}
	    } // using
	    MessageWriter replywriter = new MessageWriter();
	    replywriter.Write("ChunkRecordset sent you all your data!");
	    reply = VxDbus.CreateReply(call, "s", replywriter);
        } catch (DbException e) {
            throw new VxSqlException(e.Message, e);
        }
    }

    internal static void ExecRecordset(string connid, string query,
            out VxColumnInfo[] colinfo, out object[][] data,
            out byte[][] nullity)
    {
        // XXX this is fishy

        if (query.ToLower().StartsWith("list tables"))
            query = "exec sp_tables";
        else if (query.ToLower().StartsWith("list columns "))
            query = String.Format("exec sp_columns @table_name='{0}'",
                    query.Substring(13));
        else if (query.ToLower().StartsWith("list all table") &&
                query.ToLower().StartsWith("list all tablefunction") == false)
            query = "select distinct cast(Name as varchar(max)) Name"
                + " from sysobjects "
                + " where objectproperty(id,'IsTable')=1 "
                + " and xtype='U' "
                + " order by Name ";
        else if (query.ToLower().StartsWith("list all"))
            // Format: list all {view|trigger|procedure|scalarfunction|tablefunction}
            // Returns: a list of all of whatever
            query = String.Format(
                    "select distinct "
                    + " cast (object_name(id) as varchar(256)) Name "
                    + " from syscomments "
                    + " where objectproperty(id,'Is{0}') = 1 "
                    + " order by Name ",
                    query.Split(' ')[2].Trim());
        else if (query.ToLower().StartsWith("get object"))
            // Format: 
            // get object {view|trigger|procedure|scalarfunction|tablefunction} name
            // Returns: the "source code" to the object
            query = String.Format(
                    "select cast(text as varchar(max)) text "
                    + "from syscomments "
                    + "where objectproperty(id, 'Is{0}') = 1 "
                    + "and object_name(id) = '{1}' "
                    + "order by number, colid ",
                    query.Split(' ')[2].Trim(), 
                    query.Split(' ')[3].Trim());

        log.print(WvLog.L.Debug3, "ExecRecordset {0}\n", query);

        try {
            List<object[]> rows = new List<object[]>();
            List<byte[]> rownulls = new List<byte[]>();

	    using (var dbi = VxSqlPool.create(connid))
            using (var result = dbi.select(query))
            {
		var columns = result.columns.ToArray();
		
                if (columns.Length <= 0) 
                    log.print("No columns in resulting data set.");

                colinfo = ProcessSchema(result.columns);

                foreach (var r in result)
                {
                    object[] row = new object[columns.Length];
                    byte[] rownull = new byte[columns.Length];

                    for (int i = 0; i < columns.Length; i++) 
                    {
                        bool isnull = r[i].IsNull;
                        row[i] = null;
                        rownull[i] = isnull ? (byte)1 : (byte)0;

                        switch (colinfo[i].VxColumnType) 
                        {
                            case VxColumnType.Int64:
                                row[i] = !isnull ? (Int64)r[i] : new Int64();
                                break;
                            case VxColumnType.Int32:
			        row[i] = !isnull ? (Int32)r[i] : new Int32();
                                break;
                            case VxColumnType.Int16:
                                row[i] = !isnull ? (Int16)r[i] : new Int16();
                                break;
                            case VxColumnType.UInt8:
                                row[i] = !isnull ? (Byte)r[i] : new Byte();
                                break;
                            case VxColumnType.Bool:
                                row[i] = !isnull ? (bool)r[i] : new Boolean();
                                break;
                            case VxColumnType.Double:
                                // Might return a Single or Double
                                // FIXME: Check if getting a single causes this
                                // to croak
                                row[i] = !isnull ? (double)r[i] : (double)0.0;
                                break;
                            case VxColumnType.Uuid:
                                row[i] = !isnull ? ((Guid)r[i]).ToString() : "";
                                break;
                            case VxColumnType.Binary:
			        row[i] = !isnull ? (byte[])r[i] : new byte[0];
			        break;
                            case VxColumnType.String:
                                row[i] = !isnull ? (string)r[i] : "";
                                break;
                            case VxColumnType.DateTime:
                                row[i] = !isnull ?
                                    new VxDbusDateTime(r[i]) :
                                    new VxDbusDateTime();
                                break;
                            case VxColumnType.Decimal:
                                row[i] = !isnull 
			             ? ((decimal)r[i]).ToString() : "";
                                break;
                        }
                    }

                    rows.Add(row);
                    rownulls.Add(rownull);
                }

                data = rows.ToArray();
                nullity = rownulls.ToArray();
                log.print(WvLog.L.Debug4, "({0} rows)\n", data.Length);
                wv.assert(nullity.Length == data.Length);
            }
        } catch (DbException e) {
            throw new VxSqlException(e.Message, e);
        }
    }

    private static VxColumnInfo[] ProcessSchema(IEnumerable<WvColInfo> columns)
    {
	// FIXME:  This is stupidly similar to ProcessSchema
	int ncols = columns.Count();
        VxColumnInfo[] colinfo = new VxColumnInfo[ncols];

        if (ncols <= 0) 
            return colinfo;

        int i = 0;

        foreach (WvColInfo col in columns)
	{
            System.Type type = col.type;

            if (type == typeof(object))
	    {
                // We're not even going to try to handle this yet
                throw new VxBadSchemaException("Columns of type sql_variant "
                    + "are not supported by Versaplex at this time");
            }

            VxColumnType coltype;

            if (type == typeof(Int64)) {
                coltype = VxColumnType.Int64;
            } else if (type == typeof(Int32)) {
                coltype = VxColumnType.Int32;
            } else if (type == typeof(Int16)) {
                coltype = VxColumnType.Int16;
            } else if (type == typeof(Byte)) {
                coltype = VxColumnType.UInt8;
            } else if (type == typeof(Boolean)) {
                coltype = VxColumnType.Bool;
            } else if (type == typeof(Single) || type == typeof(Double)) {
                coltype = VxColumnType.Double;
            } else if (type == typeof(Guid)) {
                coltype = VxColumnType.Uuid;
            } else if (type == typeof(Byte[])) {
                coltype = VxColumnType.Binary;
            } else if (type == typeof(string)) {
                coltype = VxColumnType.String;
            } else if (type == typeof(DateTime)) {
                coltype = VxColumnType.DateTime;
            } else if (type == typeof(Decimal)) {
                coltype = VxColumnType.Decimal;
            } else {
                throw new VxBadSchemaException("Columns of type "
                        + type.ToString() + " are not supported by "
                        + "Versaplex at this time " +
                        "(column " + col.name + ")");
            }

            colinfo[i] = new VxColumnInfo(col.name, coltype,
			  col.nullable, col.size, col.precision, col.scale);

            i++;
        }

	return colinfo;
    }
}

public class VxDbInterfaceRouter : VxInterfaceRouter 
{

    static WvLog log = new WvLog("VxDbInterfaceRouter");
    static readonly VxDbInterfaceRouter instance;
    public static VxDbInterfaceRouter Instance {
        get { return instance; }
    }

    static VxDbInterfaceRouter() {
        instance = new VxDbInterfaceRouter();
    }

    private VxDbInterfaceRouter() : base("vx.db")
    {
        methods.Add("Test", CallTest);
        methods.Add("Quit", CallQuit);
        methods.Add("ExecScalar", CallExecScalar);
        methods.Add("ExecRecordset", CallExecRecordset);
        methods.Add("ExecChunkRecordset", CallExecChunkRecordset);
        methods.Add("GetSchemaChecksums", CallGetSchemaChecksums);
        methods.Add("GetSchema", CallGetSchema);
        methods.Add("PutSchema", CallPutSchema);
        methods.Add("DropSchema", CallDropSchema);
        methods.Add("GetSchemaData", CallGetSchemaData);
        methods.Add("PutSchemaData", CallPutSchemaData);
    }

    protected override void ExecuteCall(MethodCallProcessor processor,
					Connection conn,
					Message call, out Message reply)
    {
        try {
            processor(conn, call, out reply);
        } catch (VxRequestException e) {
            reply = VxDbus.CreateError(e.DBusErrorType, e.Message, call);
            log.print("SQL result: {0}\n", e.Short());
        } catch (Exception e) {
            reply = VxDbus.CreateError(
                    "vx.db.exception", 
                    "An internal error occurred.", call);
            log.print("{0}\n", e.ToString());
        }
    }

    static Dictionary<string,string> usernames = new Dictionary<string, string>();
    
    public static string GetClientId(Message call)
    {
        string sender = call.sender;

        // For now, the client ID is just the username of the Unix UID that
        // DBus has associated with the connection.
        string username;
	var conn = VersaMain.conn;
        if (!usernames.TryGetValue(sender, out username))
        {
	    try
	    {
		// FIXME:  This will likely change as we find a more
		//   universal way to do SSL authentication via D-Bus.
		username = VxSqlPool.GetUsernameForCert(
				conn.GetCertFingerprint(sender));
	    }
	    catch
	    {
		try
		{
		    // FIXME: This system call isn't actually standard
		    username = conn.GetUnixUserName(sender);
		}
		catch
		{
		    try
		    {
			// FIXME: This system call is standard, but not useful
			//   on Windows.
			username = conn.GetUnixUser(sender).ToString();
		    }
		    catch
		    {
			username = "*"; // use default connection, if any
		    }
		}
	    }


	    
            // Remember the result, so we don't have to ask DBus all the time
            usernames[sender] = username;
	    
	    log.print(WvLog.L.Info,
		      "New connection '{0}' is user '{1}'\n",
		      sender, username);
        }

        return username;
    }

    private static Message CreateUnknownMethodReply(Message call, 
        string methodname)
    {
        return VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.UnknownMethod",
                    String.Format(
                        "No overload of {0} has signature '{1}'",
                        methodname, call.signature), call);
    }

    private static void CallTest(Connection conn,
				 Message call, out Message reply)
    {
        if (call.signature.ne()) {
            reply = CreateUnknownMethodReply(call, "Test");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        VxDb.ExecRecordset(clientid, "select 'Works! :D'", 
            out colinfo, out data, out nullity);

        // FIXME: Add vx.db.toomuchdata error
	MessageWriter writer = PrepareRecordsetWriter(colinfo, data, nullity);
	
        reply = VxDbus.CreateReply(call, "a(issnny)vaay", writer);
	
        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }

    private static void CallQuit(Connection conn,
				 Message call, out Message reply)
    {
	// FIXME: Check permissions here
        MessageWriter writer = new MessageWriter();
	writer.Write("Quit");
        reply = VxDbus.CreateReply(call, "s", writer);
	VersaMain.want_to_die = true;
	
        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }

    private static void CallExecScalar(Connection conn,
				       Message call, out Message reply)
    {
        if (call.signature != "s") {
            reply = CreateUnknownMethodReply(call, "ExecScalar");
            return;
        }

        if (call.Body == null) {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.InvalidSignature",
                    "Signature provided but no body received", call);
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        string query = it.pop();

        object result;
	VxColumnType coltype;
        VxDb.ExecScalar(clientid, (string)query,
			out coltype, out result);

        MessageWriter writer = new MessageWriter();
	writer.WriteSig(VxColumnTypeToSignature(coltype));
	WriteV(writer, coltype, result);

        reply = VxDbus.CreateReply(call, "v", writer);
	
        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }
    
    static void WriteColInfo(MessageWriter writer, VxColumnInfo[] colinfo)
    {
	// a(issnny)
	writer.WriteArray(8, colinfo, (w2, i) => {
	    w2.Write(i.size);
	    w2.Write(i.colname);
	    w2.Write(i.coltype.ToString());
	    w2.Write(i.precision);
	    w2.Write(i.scale);
	    w2.Write(i.nullable);
	});
    }

    public static void CallExecRecordset(Connection conn,
					 Message call, out Message reply)
    {
        if (call.signature != "s") {
            reply = CreateUnknownMethodReply(call, "ExecRecordset");
            return;
        }

        if (call.Body == null) {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.InvalidSignature",
                    "Signature provided but no body received", call);
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        string query = it.pop();

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        VxDb.ExecRecordset(clientid, (string)query, 
            out colinfo, out data, out nullity);

        // FIXME: Add vx.db.toomuchdata error
	MessageWriter writer = PrepareRecordsetWriter(colinfo, data, nullity);
	
        reply = VxDbus.CreateReply(call, "a(issnny)vaay", writer);

        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }

    private static void CallExecChunkRecordset(Connection conn,
					       Message call, out Message reply)
    {
	// XXX: Stuff in this comment block shamelessly stolen from
	// "CallExecRecordset".
        if (call.signature != "s") {
            reply = CreateUnknownMethodReply(call, "ExecChunkRecordset");
            return;
        }

        if (call.Body == null) {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.InvalidSignature",
                    "Signature provided but no body received", call);
            return;
        }
	/// XXX

        VxDb.ExecChunkRecordset(conn, call, out reply);
	
        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }
    
    static string VxColumnTypeToSignature(VxColumnType t)
    {
	switch (t)
	{
	case VxColumnType.Int64:
	    return "x";
	case VxColumnType.Int32:
	    return "i";
	case VxColumnType.Int16:
	    return "n";
	case VxColumnType.UInt8:
	    return "y";
	case VxColumnType.Bool:
	    return "b";
	case VxColumnType.Double:
	    return "d";
	case VxColumnType.Uuid:
	    return "s";
	case VxColumnType.Binary:
	    return "ay";
	case VxColumnType.String:
	    return "s";
	case VxColumnType.DateTime:
	    return "(xi)";
	case VxColumnType.Decimal:
	    return "s";
	default:
	    throw new ArgumentException("Unknown VxColumnType");
	}
    }

    static string VxColumnInfoToArraySignature(VxColumnInfo[] vxci)
    {
        StringBuilder sig = new StringBuilder("a(");
        foreach (VxColumnInfo ci in vxci)
	    sig.Append(VxColumnTypeToSignature(ci.VxColumnType));
        sig.Append(")");

        return sig.ToString();
    }

    private static void CallGetSchemaChecksums(Connection conn,
					       Message call, out Message reply)
    {
        if (call.signature.ne()) {
            reply = CreateUnknownMethodReply(call, "GetSchemaChecksums");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

        // FIXME: Add vx.db.toomuchdata error
        MessageWriter writer = new MessageWriter();

        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            VxSchemaChecksums sums = backend.GetChecksums();
            sums.WriteChecksums(writer);
        }

        reply = VxDbus.CreateReply(call, 
            VxSchemaChecksums.GetDbusSignature(), writer);

        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }

    private static void CallGetSchema(Connection conn,
				      Message call, out Message reply)
    {
        if (call.signature != "as") {
            reply = CreateUnknownMethodReply(call, "GetSchema");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        string[] names = it.pop().Cast<string>().ToArray();

        MessageWriter writer = new MessageWriter();

        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            VxSchema schema = backend.Get(names);
            schema.WriteSchema(writer);
        }

        reply = VxDbus.CreateReply(call, VxSchema.GetDbusSignature(), writer);

        // For debugging
        VxDbus.MessageDump(" >> ", reply);
    }

    private static void CallDropSchema(Connection conn,
				       Message call, out Message reply)
    {
        if (call.signature != "as") {
            reply = CreateUnknownMethodReply(call, "DropSchema");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
	string[] keys = it.pop().Cast<string>().ToArray();

        VxSchemaErrors errs;
        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            errs = backend.DropSchema(keys);
        }

        MessageWriter writer = new MessageWriter();
        VxSchemaErrors.WriteErrors(writer, errs);

        reply = VxDbus.CreateReply(call, VxSchemaErrors.GetDbusSignature(), 
            writer);
        if (errs != null && errs.Count > 0)
        {
            reply.type = MessageType.Error;
            reply.err = "org.freedesktop.DBus.Error.Failed";
        }
    }

    private static void CallPutSchema(Connection conn,
				      Message call, out Message reply)
    {
        if (call.signature != String.Format("{0}i", 
                VxSchema.GetDbusSignature())) {
            reply = CreateUnknownMethodReply(call, "PutSchema");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        VxSchema schema = new VxSchema(it.pop());
        int opts = it.pop();

        VxSchemaErrors errs;
        
        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            errs = backend.Put(schema, null, (VxPutOpts)opts);
        }

        MessageWriter writer = new MessageWriter();
        VxSchemaErrors.WriteErrors(writer, errs);

        reply = VxDbus.CreateReply(call, VxSchemaErrors.GetDbusSignature(), 
            writer);
        if (errs != null && errs.Count > 0)
        {
            reply.type = MessageType.Error;
            reply.err = "org.freedesktop.DBus.Error.Failed";
        }
    }

    private static void CallGetSchemaData(Connection conn,
					  Message call, out Message reply)
    {
        if (call.signature != "ss") {
            reply = CreateUnknownMethodReply(call, "GetSchemaData");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        string tablename = it.pop();
	string where = it.pop();

        MessageWriter writer = new MessageWriter();

        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            string schemadata = backend.GetSchemaData(tablename, 0, where);
            writer.Write(schemadata);
        }

        reply = VxDbus.CreateReply(call, "s", writer);
    }

    private static void CallPutSchemaData(Connection conn,
					  Message call, out Message reply)
    {
        if (call.signature != "ss") {
            reply = CreateUnknownMethodReply(call, "PutSchemaData");
            return;
        }

        string clientid = GetClientId(call);
        if (clientid == null)
        {
            reply = VxDbus.CreateError(
                    "org.freedesktop.DBus.Error.Failed",
                    "Could not identify the client", call);
            return;
        }

	var it = call.iter();
        string tablename = it.pop();
        string text = it.pop();

        using (var dbi = VxSqlPool.create(clientid))
        {
            VxDbSchema backend = new VxDbSchema(dbi);
            backend.PutSchemaData(tablename, text, 0);
        }

        reply = VxDbus.CreateReply(call);
    }
    
    static void WriteV(MessageWriter w, VxColumnType t, object v)
    {
	switch (t)
	{
	case VxColumnType.Int64:
	    w.Write((Int64)v);
	    break;
	case VxColumnType.Int32:
	    w.Write((Int32)v);
	    break;
	case VxColumnType.Int16:
	    w.Write((Int16)v);
	    break;
	case VxColumnType.UInt8:
	    w.Write((byte)v);
	    break;
	case VxColumnType.Bool:
	    w.Write((bool)v);
	    break;
	case VxColumnType.Double:
	    w.Write((double)v);
	    break;
	case VxColumnType.Binary:
	    w.Write((byte[])v);
	    break;
	case VxColumnType.String:
	case VxColumnType.Decimal:
	case VxColumnType.Uuid:
	    w.Write((string)v);
	    break;
	case VxColumnType.DateTime:
	    {
		var dt = (VxDbusDateTime)v;
		w.Write(dt.seconds);
		w.Write(dt.microseconds);
		break;
	    }
	default:
	    throw new ArgumentException("Unknown VxColumnType");
	}
    }

    // a(issnny)vaay
    public static MessageWriter PrepareRecordsetWriter(VxColumnInfo[] colinfo,
						object[][] data,
						byte[][] nulldata)
    {
	MessageWriter writer = new MessageWriter();
	
	// a(issnny)
	WriteColInfo(writer, colinfo);
	
	// v
	if (colinfo.Length <= 0)
	{
	    // Some clients can't parse a() (empty struct) properly, so
	    // we'll have an empty array of (i) instead.
	    writer.WriteSig("a(i)");
	}
	else
	    writer.WriteSig(VxColumnInfoToArraySignature(colinfo));

	// a(whatever)
	writer.WriteArray(8, data, (w2, r) => {
	    for (int i = 0; i < colinfo.Length; i++)
		WriteV(w2, colinfo[i].VxColumnType, r[i]);
	});
	
	// aay
	writer.WriteArray(4, nulldata, (w2, r) => {
	    w2.Write(r);
	});

	return writer;
    }
}

