﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace log4net.Elasticsearch.Async
{
    public class AppenderSettings
    {
        public string Scheme { get; }

        public string User { get; }

        public string Password { get; }

        public string Server { get; }

        public string Port { get; }

        public bool IsRollingIndex { get; }

        public string Index { get; }

        public string Routing { get; }

        public string Bulk { get; }

        private readonly Lazy<Uri> _lazyUri;
        public Uri Uri => _lazyUri.Value;

        private readonly Dictionary<string, string> _settings;

        public AppenderSettings(string connectionString)
            : this(Parse(connectionString))
        {
        }

        public AppenderSettings(Dictionary<string, string> settings)
        {
            _settings = settings == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);

            // Init

            this.Scheme = TryGet("Scheme");
            this.User = TryGet("User");
            this.Password = TryGet("Pwd", "Password");
            this.Server = TryGet("Server");
            this.Port = TryGet("Port");
            this.IsRollingIndex = bool.TryParse(TryGet("Rolling"), out var isRollingIndex) ? isRollingIndex : false;

            var indexName = TryGet("Index");
            this.Index = this.IsRollingIndex
                ? $"{indexName}-{DateTime.UtcNow.ToString("yyyy.MM.dd")}"
                : indexName;

            var routingName = TryGet("Routing");
            this.Routing = !string.IsNullOrWhiteSpace(routingName)
                ? $"?routing={routingName}"
                : string.Empty;

            this.Bulk = int.TryParse(TryGet("BufferSize"), out var bufferSize) &&
                        bufferSize > 1 ? "/_bulk" : string.Empty;

            _lazyUri = new Lazy<Uri>(() =>
            {
                var sb = new StringBuilder();
                sb.Append($"{this.Scheme}://");

                if (!string.IsNullOrWhiteSpace(this.User) && !string.IsNullOrWhiteSpace(this.Password))
                    sb.Append($"{this.User}:{this.Password}@");

                sb.Append($"{this.Server}");

                if (!string.IsNullOrEmpty(this.Port))
                    sb.Append($":{this.Port}");

                sb.Append($"/{this.Index}/logEvent{this.Routing}{this.Bulk}");

                var uri = new Uri(sb.ToString());
                return uri;
            });

            // Local functions

            string TryGet(params string[] coalescingSettingsKeys)
            {
                foreach (var key in coalescingSettingsKeys)
                    if (_settings.TryGetValue(key, out var value))
                        return value;

                return string.Empty;
            }
        }

        public bool AreValid()
        {
            return
                !string.IsNullOrWhiteSpace(this.Scheme) &&
                !string.IsNullOrWhiteSpace(this.Server) &&
                !string.IsNullOrWhiteSpace(this.Index);
        }

        private static Dictionary<string, string> Parse(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return new Dictionary<string, string>();

            var csBuilder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in csBuilder.Keys)
                settings[key] = csBuilder[key].ToString();

            return settings;
        }
    }
}
