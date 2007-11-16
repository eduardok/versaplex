#include "fakeversaplex.h"

#include "table.h"
#include "wvtest.h"

#include <vector>

#include "../wvlogger.h"

int FakeVersaplexServer::num_names_registered = 0;

FakeVersaplexServer::FakeVersaplexServer() :
    vxserver_conn("dbus:session"),
    t(NULL),
    log("Fake Versaplex", WvLog::Debug1)
{
    fprintf(stderr, "*** In FakeVersaplexServer constructor\n");
    WvString use_real(getenv("USE_REAL_VERSAPLEX"));
    if (!use_real || use_real == "0") 
    {
        WvIStreamList::globallist.append(&vxserver_conn, false);

        log("*** Registering com.versabanq.versaplex\n");
        vxserver_conn.request_name("com.versabanq.versaplex", &name_request_cb);
        while (num_names_registered < 1)
            WvIStreamList::globallist.runonce();

        WvDBusCallback cb(wv::bind(
            &FakeVersaplexServer::msg_received, this, _1));
        vxserver_conn.add_callback(WvDBusConn::PriNormal, cb, this);
    }
}

FakeVersaplexServer::~FakeVersaplexServer()
{
    fprintf(stderr, "*** In FakeVersaplexServer destructor\n");
    // Dirty hack: Close any open WvLog files when we destroy a
    // FakeVersaplexServer.  
    // This keeps the WvTest open file detector from freaking out since the
    // log files are opened lazily after the open file detector does its
    // initial check.  These will get reopened if we call any relevant
    // WvLog functions again via VxODBC.  
    wvlog_close();
}
    
bool FakeVersaplexServer::msg_received(WvDBusMsg &msg)
{
    if (msg.get_dest() != "com.versabanq.versaplex")
        return false;

    if (msg.get_path() != "/com/versabanq/versaplex/db") 
        return false;

    if (msg.get_interface() != "com.versabanq.versaplex.db") 
        return false;

    // The message was for us

    log("*** Received message %s\n", (WvString)msg);
    log("*** Got argstr '%s'\n", msg.get_argstr());

    log("sender:%s\ndest:%s\npath:%s\niface:%s\nmember:%s\n",
        msg.get_sender(), msg.get_dest(), msg.get_path(), 
        msg.get_interface(), msg.get_member());

    if (msg.get_member() == "ExecRecordset")
    {
        log("Processing ExecRecordSet\n");
        WvString query(msg.get_argstr());
        if (query == "use pmccurdy")
        {
            log("*** Sending error\n");
            WvDBusError(msg, "System.ArgumentOutOfRangeException", 
                "Argument is out of range.").send(vxserver_conn);
            return false;
        }
        else if (query == expected_query)
        {
            log("*** Sending reply\n");
            WvDBusMsg reply = msg.reply();
            std::vector<Column>::iterator it;

            reply.array_start(WvString("(%s)", ColumnInfo::getDBusSignature()));
            for (it = t->cols.begin(); it != t->cols.end(); ++it)
                it->info.writeHeader(reply);
            reply.array_end();

            // Write the body signature
            if (t->cols.size() > 0)
            {
                WvString sig(t->getDBusTypeSignature());
                log("Body signature is %s\n", sig);
                reply.varray_start(WvString("(%s)", sig));
                if (t->cols[0].data.size() > 0) {
                    reply.struct_start(sig);
                    // Write the body
                    for (it = t->cols.begin(); it != t->cols.end(); ++it)
                    {
                        it->addDataTo(reply);
                    }
                    reply.struct_end();
                } 
                reply.varray_end();
            }

            // Nullity
            // FIXME: Need to send one copy per row, and properly reflect 
            // the data (not the column's overall nullability)
            reply.array_start("ay");
            if (t->cols.size() > 0 && t->cols[0].data.size() > 0)
            {
                reply.array_start("y");
                for (it = t->cols.begin(); it != t->cols.end(); ++it)
                    reply.append(it->info.nullable);
                reply.array_end();
            }
            reply.array_end();

            reply.send(vxserver_conn);
        }
        else
        {
            WvDBusError(msg, "System.NotImplemented", 
                "Not yet implemented.  Try again later.").send(vxserver_conn);
        }
    }
    else
    {
        WvDBusError(msg, "System.NotImplemented", 
            "Not yet implemented.  Try again later.").send(vxserver_conn);
    }
    return true;
}
