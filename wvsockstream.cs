using System;
using System.Net;
using System.Net.Sockets;
using Mono.Unix;
using Wv;

namespace Wv
{
    public class WvSockStream : WvStream
    {
	Socket _sock;
	protected Socket sock {
	    get {
		return _sock;
	    }
	    set {
		_sock = value;
		if (_sock != null)
		    _sock.Blocking = false;
	    }
	}

	public override bool isok { 
	    get { return (sock != null) && base.isok; }
	}

	public WvSockStream(Socket sock)
	{
	    this.sock = sock;
	}

	public override EndPoint localaddr {
	    get {
		if (!isok)
		    return null;
		return sock.LocalEndPoint;
	    }
	}

	public override EndPoint remoteaddr {
	    get {
		if (!isok)
		    return null;
		return sock.RemoteEndPoint;
	    }
	}

	public override int read(WvBytes b)
	{
	    if (!isok)
		return 0;

	    try
	    {
		int ret = sock.Receive(b.bytes, b.start, b.len, 0);
		if (ret <= 0) // EOF
		{
		    nowrite();
		    noread();
		    return 0;
		}
		else
		    return ret;
	    }
	    catch (SocketException e)
	    {
		if (e.ErrorCode == 10004) // EINTR is normal when non-blocking
		    return 0;
		else if (e.ErrorCode == 10035) // EWOULDBLOCK too
		    return 0;
		else
		{
		    err = e;
		    // err = new Exception(wv.fmt("Error code {0}\n", e.ErrorCode));
		}
		return 0;
	    }
	}

	public override int write(WvBytes b)
	{
	    if (!isok)
		return 0;

	    int ret = sock.Send(b.bytes, b.start, b.len, 0);
	    if (ret < 0) // Error
	    {
		err = new Exception("Write error"); // FIXME
		return 0;
	    }
	    else
		return ret;
	}
	
	public override event Action onreadable {
	    add { base.onreadable += value;
		  ev.onreadable(sock, do_readable); }
	    remove { base.onreadable -= value;
		     if (!can_onreadable) ev.onreadable(sock, null); }
	}

	public override event Action onwritable {
	    add { base.onwritable += value;
		  ev.onwritable(sock, do_writable); }
	    remove { base.onwritable -= value;
		     if (!can_onwritable) ev.onwritable(sock, null); }
	}

	void tryshutdown(SocketShutdown sd)
	{
	    try
	    {
		sock.Shutdown(sd);
	    }
	    catch (SocketException)
	    {
		// ignore
	    }
	}

	public override void noread()
	{
	    base.noread();
	    if (sock != null)
		tryshutdown(SocketShutdown.Receive);
	    ev.onreadable(sock, null);
	}

	public override void nowrite()
	{
	    base.nowrite();
	    if (sock != null)
		tryshutdown(SocketShutdown.Send);
	    ev.onwritable(sock, null);
	}

	public override void close()
	{
	    base.close();
	    if (sock != null)
	    {
		tryshutdown(SocketShutdown.Both);
		sock.Close();
		((IDisposable)sock).Dispose();
		sock = null;
	    }
	}
    }

    public class WvTcp : WvSockStream
    {
        public WvTcp(string remote, ushort port) : base(null)
	{
	    // FIXME: do DNS lookups asynchronously?
	    try
	    {
		IPHostEntry ipe = Dns.GetHostEntry(remote);
		IPEndPoint ipep = new IPEndPoint(ipe.AddressList[0], port);
		Socket sock = new Socket(AddressFamily.InterNetwork,
					 SocketType.Stream,
					 ProtocolType.Tcp);
		sock.Connect(ipep);
		this.sock = sock;
	    }
	    catch (Exception e)
	    {
		err = e;
	    }
	}
    }
    
    public class WvUnix : WvSockStream
    {	
        public WvUnix(string path) : base(null)
	{
	    EndPoint ep;
	    
	    if (path.StartsWith("@"))
		ep = new AbstractUnixEndPoint(path.Substring(1));
	    else
		ep = new UnixEndPoint(path);
	    
	    Socket sock = new Socket(AddressFamily.Unix,
				     SocketType.Stream, 0);
	    sock.Connect(ep);
	    this.sock = sock;
	}
    }
}