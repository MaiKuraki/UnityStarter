using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Proxima
{
    internal class ProximaFileServer
    {
        private Dictionary<string, ProximaStatic.StaticFile> _pathToFile;

        public struct FileResponse
        {
            public bool FileChanged;
            public string LastModifiedTime;
            public string ContentType;
            public byte[] Bytes;
        }

        public ProximaFileServer()
        {
            var staticFiles = Resources.Load<ProximaStatic>("Proxima/web");
            _pathToFile = new Dictionary<string, ProximaStatic.StaticFile>();
            foreach (var file in staticFiles.Files)
            {
                _pathToFile.Add(file.Path, file);
            }
        }

        public FileResponse? GetFileResponse(string url, string ifModifiedSince)
        {
            var path = url.Split('?')[0];
            path = path == "/" ? "index.html" : path.Substring(1);

            Log.Verbose("FileServer: " + url + " -> " + path);

            if (_pathToFile.TryGetValue(path, out var file) || _pathToFile.TryGetValue(path + ".html", out file))
            {
                var res = new FileResponse();
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var lastModifiedDt = epoch.AddMilliseconds(file.LastModified);
                if (!string.IsNullOrWhiteSpace(ifModifiedSince))
                {
                    try
                    {
                        if (HttpDateParse.ParseHttpDate(ifModifiedSince, out var ifModifiedSinceDt))
                        {
                            ifModifiedSinceDt = ifModifiedSinceDt.ToUniversalTime();
                            if (lastModifiedDt <= ifModifiedSinceDt)
                            {
                                Log.Verbose("File not modified: " + path);
                                return res;
                            }
                        }
                    } catch (Exception) {}
                }

                res.FileChanged = true;
                res.LastModifiedTime = string.Format("{0:ddd, dd MMM yyyy HH:mm:ss} GMT", lastModifiedDt);
                res.ContentType = ProximaMimeTypes.Get(Path.GetExtension(file.Path));
                res.Bytes = file.Bytes;
                Log.Verbose("File found: " + path);
                return res;
            }

            Log.Verbose("File not found: " + path);

            return null;
        }
    }
}