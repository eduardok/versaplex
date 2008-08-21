#include "wvssl_necessities.h"
#include "wvmoniker.h"
#include "wvsslstream.h"
#include "wvstrutils.h"
#include "uniconfroot.h";

WvX509Mgr *clicert = NULL;

/* So like... what the fsck?  Why, if we're overloading the SSL creation
 * moniker anyway, don't we pass in a suitable function for the callback, and
 * instead use the global assignment?  Well, good question.
 *
 * There is also an sslcert moniker, which need our callback, but takes a
 * certificate already encoded on the command line.  It's possible, however
 * unlikely, that someone would want to use that method (for, oh, say, testing).
 *
 * I don't want to overload sslcert... that thing is hairy.  So, I only
 * overload SSL so that it actually uses *a* certificate, and with the global
 * assignment to global_vcb, either method will get the callback!  Yay!
 */
static IWvStream *create_ssl(WvStringParm s, IObject *obj)
{
    return new WvSSLStream(IWvStream::create(s, obj), clicert, 0, false);
}

static WvMoniker<IWvStream> ssl_override("ssl", create_ssl, true);

static char *dbus_cert = NULL;

#ifdef _WIN32
    #define data_decrypt(x) wvunprotectdata(x)
#else
    #define data_decrypt(x) x
#endif

static bool verify_server(WvX509 *, WvSSLStream *s)
{
    if (dbus_cert)
    {
	WvString pcert = s->getattr("peercert");
	if (!strcmp(dbus_cert, trim_string(pcert.edit())))
	    return true;
    }

    return false;
}

static bool inited = false;

void init_wvssl()
{
    if (inited)
	return;

    UniConfRoot conf;
#ifndef _WIN32
    #warning On Linux, testing SSL requires a vxodbc.ini file.  Check template.
    conf.mount("ini:vxodbc.ini");
#else
    conf.mount("registry:HKEY_CURRENT_USER/Software/Versabanq/VxODBC");
#endif

    if (conf.isok() && conf["cert"].exists() && conf["privrsa"].exists())
    {
	clicert = new WvX509Mgr;
	clicert->decode(WvX509::CertPEM, data_decrypt(*conf["cert"]));
	clicert->decode(WvRSAKey::RsaPEM, data_decrypt(*conf["privrsa"]));

	if (!clicert->test())
	    WVRELEASE(clicert);
    }

    if (conf.isok() && conf["dbuscert"].exists())
    {
	WvString dcert = *conf["dbuscert"];
	dbus_cert = new char[dcert.len() + 1];
	dbus_cert = trim_string(strcpy(dbus_cert, dcert.cstr()));
	WvSSLStream::global_vcb = verify_server;
    }

    inited = true;
}

void cleanup_wvssl()
{
    WVRELEASE(clicert);
    WvSSLStream::global_vcb = NULL;
    delete [] dbus_cert;
    inited = false;
}