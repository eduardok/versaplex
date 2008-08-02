using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NDesk.DBus;
using Wv;
using Wv.Extensions;

internal static class Schemamatic
{
    static WvLog log = new WvLog("Schemamatic", WvLog.L.Debug2);

    internal static string RetrieveProcSchemasQuery(string type, int encrypted, 
        bool countonly, List<string> names)
    {
        string name_q = names.Count > 0 
            ? " and object_name(id) in ('" + names.Join("','") + "')"
            : "";

        string textcol = encrypted > 0 ? "ctext" : "text";
        string cols = countonly 
            ? "count(*)"
            : "object_name(id), colid, " + textcol + " ";

        return "select " + cols + " from syscomments " + 
            "where objectproperty(id, 'Is" + type + "') = 1 " + 
                "and encrypted = " + encrypted + name_q;
    }

    internal static void RetrieveProcSchemas(VxSchema schema, List<string> names, 
        string clientid, string type, int encrypted)
    {
        string query = RetrieveProcSchemasQuery(type, encrypted, false, names);

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        
        VxDb.ExecRecordset(clientid, query, out colinfo, out data, out nullity);

        int num = 0;
        int total = data.Length;
        foreach (object[] row in data)
        {
            num++;
            string name = (string)row[0];
            short colid = (short)row[1];
            string text;
            if (encrypted > 0)
            {
                byte[] bytes = (byte[])row[2];
                // BitConverter.ToString formats the bytes as "01-23-cd-ef", 
                // but we want to have them as just straight "0123cdef".
                text = System.BitConverter.ToString(bytes);
                text = text.Replace("-", "");
                log.print("bytes.Length = {0}, text={1}\n", bytes.Length, text);
            }
            else
                text = (string)row[2];


            // Skip dt_* functions and sys_* views
            if (name.StartsWith("dt_") || name.StartsWith("sys_"))
                continue;

            log.print("{0}/{1} {2}{3}/{4} #{5}\n", num, total, type, 
                encrypted > 0 ? "-Encrypted" : "", name, colid);
            // Fix characters not allowed in filenames
            name.Replace('/', '!');
            name.Replace('\n', '!');

            schema.Add(name, type, text, encrypted > 0);
        }
        log.print("{0}/{1} {2}{3} done\n", num, total, type, 
            encrypted > 0 ? "-Encrypted" : "");
    }

    internal static void RetrieveIndexSchemas(VxSchema schema, List<string> names, 
        string clientid)
    {
        string idxnames = (names.Count > 0) ? 
            "and ((object_name(i.object_id)+'/'+i.name) in ('" + 
                names.Join("','") + "'))"
            : "";

        string query = @"
          select 
           convert(varchar(128), object_name(i.object_id)) tabname,
           convert(varchar(128), i.name) idxname,
           convert(int, i.type) idxtype,
           convert(int, i.is_unique) idxunique,
           convert(int, i.is_primary_key) idxprimary,
           convert(varchar(128), c.name) colname,
           convert(int, ic.index_column_id) colid,
           convert(int, ic.is_descending_key) coldesc
          from sys.indexes i
          join sys.index_columns ic
             on ic.object_id = i.object_id
             and ic.index_id = i.index_id
          join sys.columns c
             on c.object_id = i.object_id
             and c.column_id = ic.column_id
          where object_name(i.object_id) not like 'sys%' 
            and object_name(i.object_id) not like 'queue_%' " + 
            idxnames + 
          @" order by i.name, i.object_id, ic.index_column_id";

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        
        VxDb.ExecRecordset(clientid, query, out colinfo, out data, out nullity);

        int old_colid = 0;
        List<string> cols = new List<string>();
        for (int ii = 0; ii < data.Length; ii++)
        {
            object[] row = data[ii];

            string tabname = (string)row[0];
            string idxname = (string)row[1];
            int idxtype = (int)row[2];
            int idxunique = (int)row[3];
            int idxprimary = (int)row[4];
            string colname = (string)row[5];
            int colid = (int)row[6];
            int coldesc = (int)row[7];

            // Check that we're getting the rows in order.
            wv.assert(colid == old_colid + 1 || colid == 1);
            old_colid = colid;

            cols.Add(coldesc == 0 ? colname : colname + " DESC");

            object[] nextrow = ((ii+1) < data.Length) ? data[ii+1] : null;
            string next_tabname = (nextrow != null) ? (string)nextrow[0] : null;
            string next_idxname = (nextrow != null) ? (string)nextrow[1] : null;
            
            // If we've finished reading the columns for this index, add the
            // index to the schema.  Note: depends on the statement's ORDER BY.
            if (tabname != next_tabname || idxname != next_idxname)
            {
                string colstr = cols.Join(",");
                string indexstr;
                if (idxprimary != 0)
                {
                    indexstr = String.Format(
                        "ALTER TABLE [{0}] ADD CONSTRAINT [{1}] PRIMARY KEY{2}\n" + 
                        "\t({3});\n\n", 
                        tabname,
                        idxname,
                        (idxtype == 1 ? " CLUSTERED" : " NONCLUSTERED"),
                        colstr);
                }
                else
                {
                    indexstr = String.Format(
                        "CREATE {0}{1}INDEX [{2}] ON [{3}] \n\t({4});\n\n",
                        (idxunique != 0 ? "UNIQUE " : ""),
                        (idxtype == 1 ? "CLUSTERED " : ""),
                        idxname,
                        tabname,
                        colstr);
                }
                schema.Add(tabname + "/" + idxname, "Index", indexstr, false);
                cols.Clear();
            }
        }
    }

    internal static string XmlSchemasQuery(int count, List<string> names)
    {
        int start = count * 4000;

        string namestr = (names.Count > 0) ? 
            "and xsc.name in ('" + names.Join("','") + "')"
            : "";

        string query = @"select sch.name owner,
           xsc.name sch, 
           cast(substring(
                 cast(XML_Schema_Namespace(sch.name,xsc.name) as varchar(max)), 
                 " + start + @", 4000) 
            as varchar(4000)) contents
          from sys.xml_schema_collections xsc 
          join sys.schemas sch on xsc.schema_id = sch.schema_id
          where sch.name <> 'sys'" + 
            namestr + 
          @" order by sch.name, xsc.name";

        return query;
    }

    internal static void RetrieveXmlSchemas(VxSchema schema, List<string> names, 
        string clientid)
    {
        bool do_again = true;
        for (int count = 0; do_again; count++)
        {
            do_again = false;
            string query = XmlSchemasQuery(count, names);

            VxColumnInfo[] colinfo;
            object[][] data;
            byte[][] nullity;
            
            VxDb.ExecRecordset(clientid, query, out colinfo, out data, 
                out nullity);

            foreach (object[] row in data)
            {
                string owner = (string)row[0];
                string name = (string)row[1];
                string contents = (string)row[2];

                if (contents == "")
                    continue;

                do_again = true;

                if (count == 0)
                    schema.Add(name, "XMLSchema", String.Format(
                        "CREATE XML SCHEMA COLLECTION [{0}].[{1}] AS '", 
                        owner, name), false);

                schema.Add(name, "XMLSchema", contents, false);
            }
        }

        // Close the quotes on all the XMLSchemas
        foreach (KeyValuePair<string, VxSchemaElement> p in schema)
        {
            if (p.Value.type == "XMLSchema")
                p.Value.text += "'\n";
        }
    }

    internal static void RetrieveTableColumns(VxSchema schema, 
        List<string> names, string clientid)
    {
        string tablenames = (names.Count > 0 
            ? "and t.name in ('" + names.Join("','") + "')"
            : "");

        string query = @"select t.name tabname,
	   c.name colname,
	   typ.name typename,
	   c.length len,
	   c.xprec xprec,
	   c.xscale xscale,
	   def.text defval,
	   c.isnullable nullable,
	   columnproperty(t.id, c.name, 'IsIdentity') isident,
	   ident_seed(t.name) ident_seed, ident_incr(t.name) ident_incr
	  from sysobjects t
	  join syscolumns c on t.id = c.id 
	  join systypes typ on c.xtype = typ.xtype
	  left join syscomments def on def.id = c.cdefault
	  where t.xtype = 'U'
	    and typ.name <> 'sysname' " + 
	    tablenames + @"
	  order by tabname, c.colorder, typ.status";

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        
        VxDb.ExecRecordset(clientid, query, out colinfo, out data, out nullity);

        List<string> cols = new List<string>();
        for (int ii = 0; ii < data.Length; ii++)
        {
            object[] row = data[ii];

            string tabname = (string)row[0];
            string colname = (string)row[1];
            string typename = (string)row[2];
            short len = (short)row[3];
            byte xprec = (byte)row[4];
            byte xscale = (byte)row[5];
            string defval = (string)row[6];
            int isnullable = (int)row[7];
            int isident = (int)row[8];
            string ident_seed = (string)row[9];
            string ident_incr = (string)row[10];

            if (isident == 0)
                ident_seed = ident_incr = null;

            string lenstr = "";
            if (typename.EndsWith("nvarchar") || typename.EndsWith("nchar"))
            {
                if (len == -1)
                    lenstr = "(max)";
                else
                {
                    len /= 2;
                    lenstr = String.Format("({0})", len);
                }
            }
            else if (typename.EndsWith("char") || typename.EndsWith("binary"))
            {
                lenstr = (len == -1 ? "(max)" : String.Format("({0})", len));
            }
            else if (typename.EndsWith("decimal") || 
                typename.EndsWith("numeric") || typename.EndsWith("real"))
            {
                lenstr = String.Format("({0},{1})", xprec,xscale);
            }

            if (defval != null && defval != "")
            {
                // MSSQL returns default values wrapped in ()s
                if (defval[0] == '(' && defval[defval.Length - 1] == ')')
                    defval = defval.Substring(1, defval.Length - 2);
            }

            cols.Add(String.Format("[{0}] [{1}]{2}{3}{4}{5}",
                colname, typename, 
                ((lenstr != "") ? " " + lenstr : ""),
                ((defval != "") ? " DEFAULT " + defval : ""),
                ((isnullable != 0) ? " NULL" : " NOT NULL"),
                ((isident != 0) ?  String.Format(
                    " IDENTITY({0},{1})", ident_seed, ident_incr) :
                    "")));

            string next_tabname = ((ii+1) < data.Length ? 
                (string)data[ii+1][0] : null);
            if (tabname != next_tabname)
            {
                string tablestr = String.Format(
                    "CREATE TABLE [{0}] (\n\t{1});\n\n",
                    tabname, cols.Join(",\n\t"));
                schema.Add(tabname, "Table", tablestr, false);

                cols.Clear();
            }
        }
    }

    // Returns a blob of text that can be used with PutSchemaData to fill 
    // the given table.
    internal static string GetSchemaData(string clientid, string tablename)
    {
        string query = "SELECT * FROM " + tablename;

        VxColumnInfo[] colinfo;
        object[][] data;
        byte[][] nullity;
        
        VxDb.ExecRecordset(clientid, query, out colinfo, out data, out nullity);

        List<string> cols = new List<string>();
        foreach (VxColumnInfo ci in colinfo)
            cols.Add("[" + ci.ColumnName + "]");

        string prefix = String.Format("INSERT INTO {0} ({1}) VALUES (", 
            tablename, cols.Join(","));

        StringBuilder result = new StringBuilder();
        List<string> values = new List<string>();
        for(int ii = 0; ii < data.Length; ii++)
        {
            object[] row = data[ii];
            values.Clear();
            for (int jj = 0; jj < row.Length; jj++)
            {
                object elem = row[jj];
                VxColumnInfo ci = colinfo[jj];
                log.print("Col {0}, name={1}, type={2}\n", jj, 
                    ci.ColumnName, ci.VxColumnType.ToString());
                if (elem == null)
                    values.Add("NULL");
                else if (ci.VxColumnType == VxColumnType.String ||
                    ci.VxColumnType == VxColumnType.DateTime)
                {
                    // Double-quote chars for SQL safety
                    string esc = elem.ToString().Replace("'", "''");
                    values.Add("'" + esc + "'");
                }
                else
                    values.Add(elem.ToString());
            }
            result.Append(prefix + values.Join(",") + ");\n");
        }

        return result.ToString();
    }

    // Delete all rows from the given table and replace them with the given
    // data.  text is an opaque hunk of text returned from GetSchemaData.
    internal static void PutSchemaData(string clientid, string tablename, 
        string text)
    {
        object result;
        VxDb.ExecScalar(clientid, String.Format("DELETE FROM [{0}]", tablename),
            out result);
        VxDb.ExecScalar(clientid, text, out result);
    }
}
