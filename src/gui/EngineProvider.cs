using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

internal static class EngineProvider
{
    static string HashFile(string path)
    {
        using(var input=File.OpenRead(path))using(var hash=SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
    }

    internal static string GetEnginePath()
    {
        // This override is for isolated smoke tests. Normal users use LocalAppData.
        string root=Environment.GetEnvironmentVariable("NOMADDRIVE_CONVERTER_RUNTIME_DIR");
        if(string.IsNullOrWhiteSpace(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NomadDriveSaveConverter","Runtime");
        root=Path.GetFullPath(root);
        string directory=Path.Combine(root,BuildInfo.Version,BuildInfo.EngineSha256);
        string target=Path.Combine(directory,"converter-engine.exe");
        using(var mutex=new Mutex(false,"Local\\NomadDriveSaveConverter_"+BuildInfo.EngineSha256.Substring(0,24)))
        {
            bool locked=false;
            try {
                try{locked=mutex.WaitOne(TimeSpan.FromSeconds(30));}catch(AbandonedMutexException){locked=true;}
                if(!locked)throw new IOException("Another converter is preparing the engine. Try again shortly.");
                Directory.CreateDirectory(directory);
                if(File.Exists(target)&&HashFile(target)==BuildInfo.EngineSha256)return target;
                string temporary=Path.Combine(directory,"engine-"+Guid.NewGuid().ToString("N")+".tmp");
                try {
                    using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Engine")){
                        if(resource==null)throw new IOException("The embedded conversion engine is missing.");
                        using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))resource.CopyTo(output);
                    }
                    if(HashFile(temporary)!=BuildInfo.EngineSha256)throw new IOException("The embedded engine failed its integrity check.");
                    if(File.Exists(target))File.Delete(target);
                    File.Move(temporary,target);
                    return target;
                } finally {if(File.Exists(temporary))File.Delete(temporary);}
            } finally {if(locked)mutex.ReleaseMutex();}
        }
    }
}
