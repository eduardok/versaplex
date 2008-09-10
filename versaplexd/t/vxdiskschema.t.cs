#include "wvtest.cs.h"
// Test the VxDiskSchema backend

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using Wv;
using Wv.Extensions;
using Wv.Test;
using NDesk.DBus;

[TestFixture]
class DiskSchemaTests : SchemamaticTester
{
    VxDbusSchema dbus;
    WvLog log;

    public DiskSchemaTests()
    {
        dbus = new VxDbusSchema(bus);
        log = new WvLog("Schemamatic Tests");
    }

    [Test, Category("Schemamatic"), Category("DropSchema"), Category("DiskBackend")]
    public void TestDropSchemaFromDisk()
    {
        string tmpdir = GetTempDir();
        try
        {
            Directory.CreateDirectory(tmpdir);
            VxDiskSchema backend = new VxDiskSchema(tmpdir);

            VxSchema schema = new VxSchema();
            schema.Add("Table", "Foo", "Foo contents", false);
            schema.Add("Table", "Bar", "Bar contents", false);
            schema.Add("Procedure", "Func1", "Func1 contents", false);
            schema.Add("Index", "Foo/Index1", "Index1 contents", false);
            schema.Add("ScalarFunction", "Func2", "Func2 contents", false);

            VxSchemaChecksums sums = new VxSchemaChecksums();
            sums.AddSum("Table/Foo", 1);
            sums.AddSum("Table/Bar", 2);
            sums.AddSum("Procedure/Func1", 3);
            sums.AddSum("Index/Foo/Index1", 4);
            sums.AddSum("ScalarFunction/Func2", 5);

            backend.Put(schema, sums, VxPutOpts.None);

            WVPASS(File.Exists(Path.Combine(tmpdir, "Table/Foo")));
            WVPASS(File.Exists(Path.Combine(tmpdir, "Table/Bar")));
            WVPASS(File.Exists(Path.Combine(tmpdir, "Procedure/Func1")));
            WVPASS(File.Exists(Path.Combine(tmpdir, "Index/Foo/Index1")));
            WVPASS(File.Exists(Path.Combine(tmpdir, "ScalarFunction/Func2")));

            VxSchema newschema = backend.Get(null);
            VxSchemaChecksums newsums = backend.GetChecksums();

            WVPASSEQ(newschema.Count, schema.Count);
            WVPASSEQ(newsums.Count, sums.Count);
            WVPASS(newschema.ContainsKey("Table/Foo"));
            WVPASS(newschema.ContainsKey("Table/Bar"));
            WVPASS(newschema.ContainsKey("Procedure/Func1"));
            WVPASS(newschema.ContainsKey("Index/Foo/Index1"));
            WVPASS(newschema.ContainsKey("ScalarFunction/Func2"));

            string[] todrop = { "Table/Foo", "Index/Foo/Index1" };
            backend.DropSchema(todrop);

            WVPASS(!File.Exists(Path.Combine(tmpdir, "Table/Foo")))
            WVPASS(File.Exists(Path.Combine(tmpdir, "Table/Bar")))
            WVPASS(File.Exists(Path.Combine(tmpdir, "Procedure/Func1")))
            WVPASS(!File.Exists(Path.Combine(tmpdir, "Index/Foo/Index1")))
            WVPASS(!Directory.Exists(Path.Combine(tmpdir, "Index/Foo")))
            WVPASS(File.Exists(Path.Combine(tmpdir, "ScalarFunction/Func2")))

            newschema = backend.Get(null);
            newsums = backend.GetChecksums();
            WVPASSEQ(newschema.Count, 3);
            WVPASSEQ(newsums.Count, 3);
            WVPASS(!newschema.ContainsKey("Table/Foo"));
            WVPASS(newschema.ContainsKey("Table/Bar"));
            WVPASS(newschema.ContainsKey("Procedure/Func1"));
            WVPASS(!newschema.ContainsKey("Index/Foo/Index1"));
            WVPASS(newschema.ContainsKey("ScalarFunction/Func2"));

            todrop = new string[] { "Procedure/Func1", "ScalarFunction/Func2" };
            backend.DropSchema(todrop);

            WVPASS(!File.Exists(Path.Combine(tmpdir, "Table/Foo")))
            WVPASS(File.Exists(Path.Combine(tmpdir, "Table/Bar")))
            WVPASS(!File.Exists(Path.Combine(tmpdir, "Procedure/Func1")))
            WVPASS(!File.Exists(Path.Combine(tmpdir, "Index/Foo/Index1")))
            WVPASS(!Directory.Exists(Path.Combine(tmpdir, "Index/Foo")))
            WVPASS(!File.Exists(Path.Combine(tmpdir, "ScalarFunction/Func2")))

            newschema = backend.Get(null);
            newsums = backend.GetChecksums();
            WVPASSEQ(newschema.Count, 1);
            WVPASSEQ(newsums.Count, 1);
            WVPASS(!newschema.ContainsKey("Table/Foo"));
            WVPASS(newschema.ContainsKey("Table/Bar"));
            WVPASS(!newschema.ContainsKey("Procedure/Func1"));
            WVPASS(!newschema.ContainsKey("ScalarFunction/Func2"));
        }
        finally
        {
            Directory.Delete(tmpdir, true);
            WVPASS(!Directory.Exists(tmpdir));
        }
    }

    [Test, Category("Schemamatic"), Category("PutSchemaData"), Category("DiskBackend")]
    public void TestDiskPutData()
    {
        string tmpdir = GetTempDir();
        try
        {
            Directory.CreateDirectory(tmpdir);
            VxDiskSchema backend = new VxDiskSchema(tmpdir);

            string filename = "10100-TestTable.sql";
            string datadir = Path.Combine(tmpdir, "DATA");
            string fullpath = Path.Combine(datadir, filename);
            string contents = "Random\nContents\n";

            backend.PutSchemaData("TestTable", contents, 10100);
            WVPASS(Directory.Exists(datadir));
            WVPASS(File.Exists(fullpath));
            WVPASSEQ(File.ReadAllText(fullpath), contents);

            WVPASSEQ(backend.GetSchemaData("TestTable", 10100, ""), contents);
        }
        finally
        {
            Directory.Delete(tmpdir, true);
            WVPASS(!Directory.Exists(tmpdir));
        }

    }

    [Test, Category("Schemamatic"), Category("DiskBackend")]
    public void TestExportEmptySchema()
    {
        string tmpdir = GetTempDir();

        try 
        {
            Directory.CreateDirectory(tmpdir);

            VxSchema schema = new VxSchema();
            VxSchemaChecksums sums = new VxSchemaChecksums();

            // Check that exporting an empty schema doesn't touch anything.
            VxDiskSchema backend = new VxDiskSchema(tmpdir);
            backend.Put(schema, sums, VxPutOpts.None);
            WVPASSEQ(Directory.GetDirectories(tmpdir).Length, 0);
            WVPASSEQ(Directory.GetFiles(tmpdir).Length, 0);
        }
        finally
        {
            Directory.Delete(tmpdir);
            WVASSERT(!Directory.Exists(tmpdir));
        }
    }

    private void CheckExportedFileContents(string filename, string header, 
        string text)
    {
        WVPASS(File.Exists(filename));
        using (StreamReader sr = new StreamReader(filename))
        {
            WVPASSEQ(sr.ReadLine(), header);
            string line;
            StringBuilder sb = new StringBuilder();
            while ((line = sr.ReadLine()) != null)
                sb.Append(line + "\n");
            WVPASSEQ(sb.ToString(), text);
        }
    }

    private void VerifyExportedSchema(string exportdir, VxSchema schema, 
        VxSchemaChecksums sums, SchemaCreator sc, int backupnum)
    {
        DirectoryInfo dirinfo = new DirectoryInfo(exportdir);

        int filemultiplier = backupnum + 1;
        string suffix = backupnum == 0 ? "" : "-" + backupnum;

        string procdir = Path.Combine(exportdir, "Procedure");
        string scalardir = Path.Combine(exportdir, "ScalarFunction");
        string idxdir = Path.Combine(exportdir, "Index");
        string tabdir = Path.Combine(exportdir, "Table");
        string xmldir = Path.Combine(exportdir, "XMLSchema");

        WVPASSEQ(dirinfo.GetDirectories().Length, 5);
        WVPASSEQ(dirinfo.GetFiles().Length, 0);
        WVPASS(Directory.Exists(procdir));
        WVPASS(Directory.Exists(scalardir));
        WVPASS(Directory.Exists(idxdir));
        WVPASS(Directory.Exists(tabdir));
        WVPASS(Directory.Exists(xmldir));

        // Procedures
        WVPASSEQ(Directory.GetDirectories(procdir).Length, 0);
        WVPASSEQ(Directory.GetFiles(procdir).Length, 1 * filemultiplier);
        string func1file = Path.Combine(procdir, "Func1" + suffix);
        CheckExportedFileContents(func1file, 
            "!!SCHEMAMATIC 2ae46ac0748aede839fb9cd167ea1180 0xd983a305 ",
            sc.func1q);

        // Scalar functions
        WVPASSEQ(Directory.GetDirectories(scalardir).Length, 0);
        WVPASSEQ(Directory.GetFiles(scalardir).Length, 1 * filemultiplier);
        string func2file = Path.Combine(scalardir, "Func2" + suffix);
        CheckExportedFileContents(func2file, 
            "!!SCHEMAMATIC c7c257ba4f7817e4e460a3cef0c78985 0xd6fe554f ",
            sc.func2q);

        // Indexes
        WVPASSEQ(Directory.GetDirectories(idxdir).Length, 1);
        WVPASSEQ(Directory.GetFiles(idxdir).Length, 0);

        string tab1idxdir = Path.Combine(idxdir, "Tab1");
        WVPASS(Directory.Exists(tab1idxdir));
        WVPASSEQ(Directory.GetDirectories(tab1idxdir).Length, 0);
        WVPASSEQ(Directory.GetFiles(tab1idxdir).Length, 2 * filemultiplier);

        string idx1file = Path.Combine(tab1idxdir, "Idx1" + suffix);
        CheckExportedFileContents(idx1file, 
            "!!SCHEMAMATIC 7dddb8e70153e62bb6bb3c59b7f53a4c 0x1d32c7ea 0x968dbedc ", 
            sc.idx1q);

        string pk_name = CheckForPrimaryKey(schema, "Tab1");
        string pk_file = Path.Combine(tab1idxdir, pk_name + suffix);
        string pk_query = String.Format(
            "ALTER TABLE [Tab1] ADD CONSTRAINT [{0}] " + 
            "PRIMARY KEY CLUSTERED\n" +
            "\t(f1);\n\n", pk_name);

        // We can't know the primary key's MD5 ahead of time, so compute
        // it ourselves.
        byte[] md5 = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(
            pk_query));
        string md5str = md5.ToHex().ToLower();

        CheckExportedFileContents(pk_file, 
            String.Format("!!SCHEMAMATIC {0} {1} ", 
                md5str, sums["Index/Tab1/" + pk_name].GetSumString()),
            pk_query);

        // Tables
        WVPASSEQ(Directory.GetDirectories(tabdir).Length, 0);
        WVPASSEQ(Directory.GetFiles(tabdir).Length, 2 * filemultiplier);

        string tab1file = Path.Combine(tabdir, "Tab1" + suffix);
        string tab2file = Path.Combine(tabdir, "Tab2" + suffix);

        WVPASS(File.Exists(tab1file));
        CheckExportedFileContents(tab1file, 
            "!!SCHEMAMATIC 5f79eae887634d744f1ea2db7a25994d " + 
                sums["Table/Tab1"].GetSumString() + " ",
            sc.tab1q.Replace(" PRIMARY KEY", ""));

        WVPASS(File.Exists(tab2file));
        CheckExportedFileContents(tab2file, 
            "!!SCHEMAMATIC 436efde94964e924cb0ccedb96970aff " +
            sums["Table/Tab2"].GetSumString() + " ", sc.tab2q);

        // XML Schemas
        WVPASSEQ(Directory.GetDirectories(xmldir).Length, 0);
        WVPASSEQ(Directory.GetFiles(xmldir).Length, 1 * filemultiplier);

        string testschemafile = Path.Combine(xmldir, "TestSchema" + suffix);
        WVPASS(File.Exists(testschemafile));
        CheckExportedFileContents(testschemafile, 
            "!!SCHEMAMATIC f45c4ea54c268c91f41c7054c8f20bc9 0xf4b2c764 ",
            sc.xmlq);
    }

    [Test, Category("Schemamatic"), Category("DiskBackend")]
    public void TestExportSchema()
    {
        SchemaCreator sc = new SchemaCreator(this);
        sc.Create();

        string tmpdir = GetTempDir();

        DirectoryInfo tmpdirinfo = new DirectoryInfo(tmpdir);
        try
        {
            tmpdirinfo.Create();

            // Check that having mangled checksums fails
            VxSchema schema = dbus.Get();
            VxSchemaChecksums sums = new VxSchemaChecksums();

            VxDiskSchema disk = new VxDiskSchema(tmpdir);
            try {
                WVEXCEPT(disk.Put(schema, sums, VxPutOpts.None));
            } catch (Wv.Test.WvAssertionFailure e) {
                throw e;
            } catch (System.Exception e) {
                WVPASS(e is ArgumentException);
                WVPASS(e.Message.StartsWith("Missing checksum for "));
                log.print(e.ToString() + "\n");
            }

            // Check that the normal exporting works.
            sums = dbus.GetChecksums();
            disk.Put(schema, sums, VxPutOpts.None);

            int backup_generation = 0;
            VerifyExportedSchema(tmpdir, schema, sums, sc, backup_generation);

            // Check that we read back the same stuff
            VxSchema schemafromdisk = disk.Get(null);
            VxSchemaChecksums sumsfromdisk = disk.GetChecksums();

            TestSchemaEquality(schema, schemafromdisk);
            TestChecksumEquality(sums, sumsfromdisk);

            // Doing it twice doesn't change anything.
            disk.Put(schema, sums, VxPutOpts.None);

            VerifyExportedSchema(tmpdir, schema, sums, sc, backup_generation);

            // Check backup mode
            disk.Put(schema, sums, VxPutOpts.IsBackup);
            backup_generation++;

            VerifyExportedSchema(tmpdir, schema, sums, sc, backup_generation);

            // Check backup mode again
            disk.Put(schema, sums, VxPutOpts.IsBackup);
            backup_generation++;

            VerifyExportedSchema(tmpdir, schema, sums, sc, backup_generation);
        }
        finally
        {
            tmpdirinfo.Delete(true);
            WVASSERT(!tmpdirinfo.Exists);

            sc.Cleanup();
        }
    }

    [Test, Category("Schemamatic"), Category("DiskBackend")]
    public void TestReadChecksums()
    {
        SchemaCreator sc = new SchemaCreator(this);
        sc.Create();

        string tmpdir = GetTempDir();

        DirectoryInfo tmpdirinfo = new DirectoryInfo(tmpdir);
        try
        {
            tmpdirinfo.Create();

            VxSchema schema = dbus.Get();
            VxSchemaChecksums sums = dbus.GetChecksums();
            VxDiskSchema backend = new VxDiskSchema(tmpdir);
            backend.Put(schema, sums, VxPutOpts.None);

            VxSchemaChecksums fromdisk = backend.GetChecksums();

            foreach (KeyValuePair<string, VxSchemaChecksum> p in sums)
            {
                WVPASSEQ(p.Value.GetSumString(), fromdisk[p.Key].GetSumString());
            }
            WVPASSEQ(sums.Count, fromdisk.Count);

            // Test that changing a file invalidates its checksums, and that
            // we skip directories named "DATA"
            using (StreamWriter sw = File.AppendText(
                wv.PathCombine(tmpdir, "Table", "Tab1")))
            {
                sw.WriteLine("Ooga Booga");
            }

            Directory.CreateDirectory(Path.Combine(tmpdir, "DATA"));
            File.WriteAllText(wv.PathCombine(tmpdir, "DATA", "Decoy"),
                "Decoy file, shouldn't have checksums");

            VxSchemaChecksums mangled = backend.GetChecksums();

            // Check that the decoy file didn't get read
            WVFAIL(mangled.ContainsKey("DATA/Decoy"));

            // Check that the mangled checksums exist, but are empty.
            WVASSERT(mangled.ContainsKey("Table/Tab1"));
            WVASSERT(mangled["Table/Tab1"].GetSumString() != 
                sums["Table/Tab1"].GetSumString());
            WVPASSEQ(mangled["Table/Tab1"].GetSumString(), "");

            // Check that everything else is still sensible
            foreach (KeyValuePair<string, VxSchemaChecksum> p in sums)
            {
                if (p.Key != "Table/Tab1")
                    WVPASSEQ(p.Value.GetSumString(), 
                        mangled[p.Key].GetSumString());
            }
        }
        finally
        {
            tmpdirinfo.Delete(true);
            WVASSERT(!tmpdirinfo.Exists);

            sc.Cleanup();
        }
    }

    [Test, Category("Schemamatic"), Category("DiskBackend")]
    public void TestSubmoduleSupport()
    {
        VxSchema schema1 = new VxSchema();
        VxSchemaChecksums sums1 = new VxSchemaChecksums();

        VxSchema schema2 = new VxSchema();
        VxSchemaChecksums sums2 = new VxSchemaChecksums();

        schema1.Add("Table", "Tab1", "Random contents", false);
        sums1.AddSum("Table/Tab1", 1);
        schema2.Add("Table", "Tab2", "Random contents 2", false);
        sums2.AddSum("Table/Tab2", 2);

        string tmpdir = GetTempDir();
        try
        {
            Directory.CreateDirectory(tmpdir);
            VxDiskSchema disk = new VxDiskSchema(tmpdir);

            disk.Put(schema2, sums2, VxPutOpts.None);

            VxDiskSchema.AddFromDir(tmpdir, schema1, sums1);

            WVPASSEQ(sums1["Table/Tab1"].GetSumString(), "0x00000001");
            WVPASSEQ(schema1["Table/Tab1"].name, "Tab1");
            WVPASSEQ(schema1["Table/Tab1"].type, "Table");
            WVPASSEQ(schema1["Table/Tab1"].text, "Random contents");
            WVPASSEQ(schema1["Table/Tab1"].encrypted, false);

            WVPASSEQ(sums1["Table/Tab2"].GetSumString(), "0x00000002");
            WVPASSEQ(schema1["Table/Tab2"].name, "Tab2");
            WVPASSEQ(schema1["Table/Tab2"].type, "Table");
            WVPASSEQ(schema1["Table/Tab2"].text, "Random contents 2");
            WVPASSEQ(schema1["Table/Tab2"].encrypted, false);
        }
        finally
        {
            Directory.Delete(tmpdir, true);
            WVPASS(!Directory.Exists(tmpdir));
        }

    }

    [Test, Category("Schemamatic"), Category("DiskBackend")]
    public void TestSubmoduleExceptions()
    {
        VxSchema schema1 = new VxSchema();
        VxSchemaChecksums sums1 = new VxSchemaChecksums();

        VxSchema schema2 = new VxSchema();
        VxSchemaChecksums sums2 = new VxSchemaChecksums();

        schema1.Add("Procedure", "Func1", "Random contents", false);
        sums1.AddSum("Procedure/Func1", 1);
        schema2.Add("Procedure", "Func1", "Random contents 2", false);
        sums2.AddSum("Procedure/Func1", 2);

        string tmpdir = GetTempDir();
        try
        {
            Directory.CreateDirectory(tmpdir);
            VxDiskSchema disk = new VxDiskSchema(tmpdir);

            disk.Put(schema2, sums2, VxPutOpts.None);

            try {
                WVEXCEPT(VxDiskSchema.AddFromDir(tmpdir, schema1, sums1))
            } catch (System.ArgumentException e) {
                WVPASSEQ(e.Message, "Conflicting schema key: Procedure/Func1");
            }
        }
        finally
        {
            Directory.Delete(tmpdir, true);
            WVPASS(!Directory.Exists(tmpdir));
        }

    }

    [Test, Category("Schemamatic"), Category("CopySchema")]
    public void TestCopySchema()
    {
        SchemaCreator sc = new SchemaCreator(this);
        sc.Create();

        string msg2 = "Hello, world, this used to be Func1!";
        string func1q2 = "create procedure Func1 as select '" + msg2 + "'\n";

        VxSchema origschema = dbus.Get();
        VxSchemaChecksums origsums = dbus.GetChecksums();

        string tmpdir = GetTempDir();
        try
        {
            Directory.CreateDirectory(tmpdir);
            VxDiskSchema disk = new VxDiskSchema(tmpdir);

            // Test that the copy function will create new elements
            VxSchema.CopySchema(dbus, disk);

            VxSchema newschema = disk.Get(null);
            VxSchemaChecksums newsums = disk.GetChecksums();

            TestSchemaEquality(origschema, newschema);
            TestChecksumEquality(origsums, newsums);

            // Test that the copy function updates changed elements, and
            // deletes old ones.
            origschema["Procedure/Func1"].text = func1q2;

            dbus.Put(origschema, null, VxPutOpts.None);
            dbus.DropSchema("Index/Tab1/Idx1");
            origschema.Remove("Index/Tab1/Idx1");
            origsums = dbus.GetChecksums();

            VxSchema.CopySchema(dbus, disk);
            newschema = disk.Get(null);
            newsums = disk.GetChecksums();

            TestSchemaEquality(origschema, newschema);
            TestChecksumEquality(origsums, newsums);
        }
        finally
        {
            Directory.Delete(tmpdir, true);
            WVPASS(!Directory.Exists(tmpdir));
        }

        sc.Cleanup();
    }

    public static void Main()
    {
        WvTest.DoMain();
    }
}
