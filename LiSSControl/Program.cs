/* 
 * Copyright (c) 2019 Daniel Kautz, TELCO TECH GmbH
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace LiSSControl
{
    class Program
    {
        private static XmlDocument RequestXml(HttpClient httpClient, Uri uri, HttpContent content, CancellationToken token)
        {
            Task<HttpResponseMessage> t = (null != content) ? httpClient.PostAsync(uri, content, token) : httpClient.GetAsync(uri, token);

            using (t)
            {
                try
                {
                    t.Wait();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Something went wrong: {ex.Message}");
                    return null;
                }

                using (HttpResponseMessage response = t.Result)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (Task<Stream> t2 = response.Content.ReadAsStreamAsync())
                        {
                            t2.Wait();

                            XmlDocument doc = new XmlDocument();
                            doc.Load(t2.Result);
                            return doc;
                        }
                    }
                }
            }

            return null;
        }

        static void Main(string[] args)
        {
            IDictionary<string, object> arguments = new Dictionary<string, object>
            {
                ["host"] = String.Empty,        /* hostname or IP address */
                ["port"] = 443,                 /* port, defaults to 443 */
                ["user"] = String.Empty,        /* username for login */
                ["password"] = String.Empty,    /* password for login */
                ["timeout"] = 3.0,              /* communication timeout */
                ["operation"] = String.Empty,   /* requested operation */
                ["thumbprint"] = String.Empty,  /* fingerprint (sha1) for self-signed certificate */
            };

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                    continue;

                if (!args[i].Contains("="))
                    continue;

                string[] kv = args[i].Split('=');

                kv[0] = kv[0].Remove(0, 2);

                if (!arguments.Keys.Contains(kv[0]))
                {
                    Console.WriteLine("Valid options are: ");

                    foreach (string key in arguments.Keys)
                        Console.Write(key + " ");

                    Console.WriteLine();

                    return;
                }

                arguments[kv[0]] = Convert.ChangeType(kv[1], arguments[kv[0]].GetType());
            }

            if (String.Empty == (string)arguments["host"])
            {
                Console.Error.WriteLine("Hostname missing");
                return;
            }

            if (String.Empty == (string)arguments["user"])
            {
                Console.Error.WriteLine("Username missing");
                return;
            }

            if (String.Empty == (string)arguments["password"])
            {
                Console.Error.WriteLine("Password missing");
                return;
            }

            if (String.Empty == (string)arguments["operation"])
            {
                Console.Error.WriteLine("Operation missing");
                return;
            }

            string path = String.Empty;

            switch (((string)arguments["operation"]).ToLower())
            {
                case "poweroff":
                    path = "path_/settings/maintenance/shutdown::poweroff";
                    break;

                case "reboot":
                    path = "path_/settings/maintenance/shutdown::reboot";
                    break;
            }

            if (String.Empty == path)
            {
                Console.Error.WriteLine("Operation not supported");
                return;
            }

            UriBuilder baseAddress = new UriBuilder("https", (string)arguments["host"], (int)arguments["port"]);

            using (HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (sslPolicyErrors == SslPolicyErrors.None)
                        return true;

                    string thumbprint = (string)arguments["thumbprint"];

                    if (String.Empty != thumbprint &&
                        certificate.Thumbprint == thumbprint)
                    {
                        return true;
                    }

                    return false;
                },
                SslProtocols = SslProtocols.Tls12,
            })
            using (HttpClient httpClient = new HttpClient(handler))
            {
                //httpClient.BaseAddress = baseAddress.Uri;
                httpClient.Timeout = TimeSpan.FromSeconds((double)arguments["timeout"]);

                /* get inital timestamp */
                XmlDocument doc = RequestXml(httpClient, baseAddress.Uri, null, CancellationToken.None);
                string timestamp = doc?.DocumentElement?.Attributes["timestamp"]?.Value;

                if (null == timestamp)
                {
                    Console.Error.WriteLine("Timestamp missing");
                    Console.ReadKey();
                    return;
                }

                /* login */
                IDictionary<string, string> parameters = new Dictionary<string, string>
                {
                    ["timestamp"] = timestamp,
                    ["seskey"] = String.Empty,
                    ["path_/auth::login"] = String.Empty,
                    [timestamp + "login"] = (string)arguments["user"],
                    [timestamp + "password"] = (string)arguments["password"],
                };

                doc = RequestXml(httpClient, baseAddress.Uri, new FormUrlEncodedContent(parameters), CancellationToken.None);
                string seskey = null;
                bool abort = false;

                if (null != doc)
                {
                    if ("rw" != doc.DocumentElement?.Attributes["mode"]?.Value)
                    {
                        Console.Error.WriteLine("Read only mode!");
                        abort = true;
                    }

                    string msg = doc.SelectSingleNode("application/dialog/statusbar/statusmessage")?.InnerText;

                    if ("OK" != msg)
                    {
                        Console.Error.WriteLine($"Error message: {msg}");
                    }

                    seskey = doc.DocumentElement?.Attributes["seskey"]?.Value;
                }

                if (null == seskey)
                {
                    Console.Error.WriteLine("Session key missing");
                    return;
                }

                if (!abort)
                {
                    /* wait a bit */
                    if (Console.KeyAvailable)
                        Console.ReadKey(true);

                    int i = 10;

                    Console.WriteLine($"Performing '{(string)arguments["operation"]}' on {baseAddress.Uri} in {i} seconds ");

                    for (; i > 0; i--)
                    {
                        Thread.Sleep(1000);

                        if (Console.KeyAvailable)
                        {
                            Console.Write("\raborted");
                            abort = true;
                            break;
                        }

                        Console.Write("\r" + i.ToString("D2"));
                    }

                    Console.WriteLine();

                    /* perform operation */
                    if (i == 0)
                    {
                        parameters = new Dictionary<string, string>
                        {
                            ["timestamp"] = timestamp,
                            ["seskey"] = seskey,
                            [path] = String.Empty,
                        };

                        doc = RequestXml(httpClient, baseAddress.Uri, new FormUrlEncodedContent(parameters), CancellationToken.None);

                        if (null != doc)
                        {
                            string msg = doc.SelectSingleNode("application/dialog/statusbar/statusmessage")?.InnerText;

                            if ("OK" != msg)
                            {
                                Console.Error.WriteLine($"Error message: {msg}");
                                abort = true;
                            }
                        }
                    }
                }

                /* logout */
                parameters = new Dictionary<string, string>
                {
                    ["timestamp"] = timestamp,
                    ["seskey"] = seskey,
                    ["path_/auth::init"] = String.Empty,
                };

                doc = RequestXml(httpClient, baseAddress.Uri, new FormUrlEncodedContent(parameters), CancellationToken.None);

                if (null != doc)
                {
                    string msg = doc.SelectSingleNode("application/dialog/statusbar/statusmessage")?.InnerText;

                    if ("OK" != msg)
                    {
                        Console.Error.WriteLine($"Error message: {msg}");
                        abort = true;
                    }
                }

                if (abort)
                {
                    Console.ReadKey();
                }
            }
        }
    }
}
