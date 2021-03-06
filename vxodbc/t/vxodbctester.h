#ifndef VXODBCTESTER_H
#define VXODBCTESTER_H

#include "wvstring.h"
#include "wvlog.h"
#include "fileutils.h"
#include "wvdbusserver.h"
#include "wvdbusconn.h"

#define WVPASS_SQL(sql) \
    do \
    { \
        if (!WvTest::start_check(__FILE__, __LINE__, #sql, SQL_SUCCEEDED(sql)))\
            ReportError(#sql, __LINE__, __FILE__); \
    } while (0)
#define WVPASS_SQL_EQ(x, y) do { if (!WVPASSEQ((x), (y))) { CheckReturn(); } } while (0)

class Table;
class WvDBusConn;
class WvDBusMsg;

class TestDBusServer
{
public:
    WvString moniker;
    WvDBusServer *s;

    TestDBusServer()
    {
        fprintf(stderr, "Creating a test DBus server.\n");
        /* WvString smoniker("unix:tmpdir=%s.dir",
                         wvtmpfilename("wvdbus-sock-")); */
	WvString smoniker("tcp:0.0.0.0");
        s = new WvDBusServer();
	s->listen(smoniker);
        moniker = s->get_addr();
        fprintf(stderr, "Server address is '%s'\n", moniker.cstr());
        WvIStreamList::globallist.append(s, false, "test-dbus-server");
    }

    ~TestDBusServer()
    {
        WVRELEASE(s);
    }
};

class VxOdbcTester
{
public:
    TestDBusServer dbus_server;
    WvDBusConn vxserver_conn;
    WvString dbus_moniker;
    Table *t;
    WvString expected_query;
    int num_names_registered;
    WvLog log;

    // Set always_create_server to true if you don't ever want to use the real
    // Versaplex server, regardless of what USE_REAL_VERSAPLEX says.
    VxOdbcTester(bool always_create_server = false);
    ~VxOdbcTester();

    bool name_request_cb(WvDBusMsg &msg); 
    bool msg_received(WvDBusMsg &msg);
};

#endif // VXODBCTESTER_H
