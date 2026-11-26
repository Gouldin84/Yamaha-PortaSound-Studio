using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace PSS790Player.Core
{
    /// <summary>
    /// Custom <see cref="BlazorWebView"/> hosting embedded <c>wwwroot/**</c> resources so single-file
    /// self-contained builds (<c>PSS-Studio.exe</c>) launch anywhere without requiring an external <c>wwwroot</c> directory.
    /// </summary>
    public sealed class SingleFileBlazorWebView : BlazorWebView
    {
        public override IFileProvider CreateFileProvider(string contentRootDir)
        {
            return new EmbeddedWwwrootFileProvider(typeof(SingleFileBlazorWebView).Assembly);
        }
    }

    internal sealed class EmbeddedWwwrootFileProvider : IFileProvider
    {
        private readonly Assembly _assembly;
        private readonly Dictionary<string, string> _resources;

        public EmbeddedWwwrootFileProvider(Assembly assembly)
        {
            _assembly = assembly;
            _resources = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(Normalize, n => n, StringComparer.OrdinalIgnoreCase);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var key = Normalize(subpath);
            if (!_resources.TryGetValue(key, out var resName) &&
                !_resources.TryGetValue("wwwroot/" + key.TrimStart('/'), out resName))
            {
                return new NotFoundFileInfo(subpath);
            }

            return new EmbeddedResourceFileInfo(_assembly, resName, Path.GetFileName(key));
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

        private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

        private sealed class EmbeddedResourceFileInfo : IFileInfo
        {
            private readonly Assembly _assembly;
            private readonly string _resourceName;
            private long? _length;

            public EmbeddedResourceFileInfo(Assembly assembly, string resourceName, string name)
            {
                _assembly = assembly;
                _resourceName = resourceName;
                Name = name;
            }

            public bool Exists => true;

            public long Length
            {
                get
                {
                    if (_length == null)
                    {
                        using var s = _assembly.GetManifestResourceStream(_resourceName);
                        _length = s?.Length ?? 0;
                    }
                    return _length.Value;
                }
            }

            public string? PhysicalPath => null;
            public string Name { get; }
            public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => _assembly.GetManifestResourceStream(_resourceName)!;
        }
    }
}
