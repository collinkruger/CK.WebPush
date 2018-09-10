using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CK.WebPush
{
    public class Startup
    {
        public static string root;
        public static int port;

        public void ConfigureServices(IServiceCollection services) { }

        private static IObservable<string> ObserveFileSystem(string root, Regex filter)
        {
            // TODO: Do disposal correctly

            var fsw = new FileSystemWatcher(root);
            fsw.IncludeSubdirectories = true;
            fsw.EnableRaisingEvents = true;

            fsw.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;

            return Observable
                   .Merge(Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Changed += x, x => fsw.Changed -= x),
                          Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Created += x, x => fsw.Created -= x),
                          Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Deleted += x, x => fsw.Deleted -= x),
                          Observable.FromEventPattern<RenamedEventHandler, FileSystemEventArgs>(x => fsw.Renamed += x, x => fsw.Renamed -= x))
                   .Select(x => x.EventArgs.FullPath.Substring(root.Length))
                   .Where(x => filter.IsMatch(x))
                   .GroupBy(x => x)
                   .SelectMany(x => x.Throttle(TimeSpan.FromSeconds(.1)))
                   .Synchronize();
        }

        private static async Task WebSocketHandler(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = null;
            var receiver = Task.Run(async () =>
            {
                while (!(result?.CloseStatus.HasValue ?? false))
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            });

            var sender = Task.Run(async () =>
            {
                var registration = ObserveFileSystem(root, new Regex("\\.css$|\\.html$|\\.js$"))
                                  .Subscribe(str =>
                                  {
                                      if (!(result?.CloseStatus.HasValue ?? false))
                                      {
                                          var linkStr = str.TrimStart('\\').Replace('\\', '/');
                                          var command = !linkStr.EndsWith(".css") ? "reload" : "fetch";

                                          var message = $"{command}|{linkStr}";

                                          var bytes = System.Text.Encoding.ASCII.GetBytes(message);
                                          var arraySegment = new ArraySegment<byte>(bytes);
                                          webSocket.SendAsync(arraySegment, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                                      }
                                  });


                while (!(result?.CloseStatus.HasValue ?? false))
                    await Task.Delay(1000);

                registration.Dispose();
            });

            await Task.WhenAny(receiver, sender);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(LogLevel.Error);
            // loggerFactory.AddDebug(LogLevel.Debug);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/js.js")
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/javascript";
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CK.WebPush.WebPush.js"))
                    using (var streamReader = new StreamReader(stream))
                    {
                        var js = streamReader.ReadToEnd();
                        var jsWithPort = js.Replace("{{port}}", port.ToString());
                        await context.Response.WriteAsync(jsWithPort);
                    }
                }
                else
                {
                    await next();
                }
            });

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                ReceiveBufferSize = 4 * 1024
            };
            app.UseWebSockets(webSocketOptions);

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await WebSocketHandler(context, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }

            });

            app.UseFileServer();
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            Startup.root = args[0];
            Startup.port = int.Parse(args[1]);

            Console.WriteLine($@"
Add To Your Page :
<script src=""http://localhost:{Startup.port}/js.js async""></script>

Add To Your Qlik Extension:
(function addwebpush() {{
    if (window.location.hostname.startsWith(""localhost""))
    {{
        const id = ""#ck-webpush-script"";
        if (!document.querySelector(id))
        {{
            const tag = document.createElement(""script"");
            tag.id = id;
            tag.src = ""http://localhost:{Startup.port}/js.js"";
            tag.async = true;
            document.head.appendChild(tag);
        }}
    }}
}}());
");

            new WebHostBuilder()
            .UseKestrel()
            .UseUrls($"http://*:{Startup.port}")
            .UseStartup<Startup>()
            .Build()
            .Run();
        }
    }
}
