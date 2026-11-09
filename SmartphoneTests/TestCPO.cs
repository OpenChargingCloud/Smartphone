/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Smartphone <https://github.com/OpenChargingCloud/Smartphone>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Collections.Concurrent;
using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// What the web payment page of a charging station operator answers once a
    /// driver paid there and it started a session: the live link of the
    /// session, at /liveLinks/{session} - and what the station sends over it,
    /// at /sessions/{session}/live, asked again and again over "https", as a
    /// test sets it. Nothing is there to ask before the test sets something.
    /// Its stop link, at /sessions/{session}/stop, takes a POST - and answers
    /// what the test sets, 200 at first.
    /// </summary>
    public sealed class TestCPO : IAsyncDisposable
    {

        #region Data

        private readonly HTTPServer                                   server;
        private volatile String?                                      live;
        private          Int32                                        liveRequests;
        private readonly ConcurrentQueue<(String Session, String Body)>  stops  = new();

        #endregion

        #region Properties

        /// <summary>
        /// Where it is served, without a slash at the end.
        /// </summary>
        public String URL
            => $"http://127.0.0.1:{server.TCPPort}";

        /// <summary>
        /// How often what the station sends was asked for.
        /// </summary>
        public Int32 LiveRequests
            => liveRequests;

        /// <summary>
        /// The stops asked for over its stop links: of which session, and
        /// what was sent.
        /// </summary>
        public IReadOnlyList<(String Session, String Body)> Stops
            => [.. stops];

        /// <summary>
        /// What its stop links answer: 200 at first.
        /// </summary>
        public HTTPStatusCode StopAnswer { get; set; } = HTTPStatusCode.OK;

        #endregion

        #region Constructor(s)

        private TestCPO(HTTPServer Server)
        {
            this.server = Server;
        }

        #endregion


        #region (static) Start()

        public static async Task<TestCPO> Start()
        {

            var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Zero);
            var cpo     = new TestCPO(server);
            var api     = server.AddHTTPAPI(HTTPPath.Root);

            api.AddHandler(HTTPPath.Parse("/liveLinks/{session}"),      request => Task.FromResult(cpo.GetLiveLink(request)),  HTTPMethod.GET);
            api.AddHandler(HTTPPath.Parse("/sessions/{session}/live"),  request => Task.FromResult(cpo.GetLive(request)),      HTTPMethod.GET);
            api.AddHandler(HTTPPath.Parse("/sessions/{session}/stop"),  request => Task.FromResult(cpo.PostStop(request)),     HTTPMethod.POST);

            await server.Start();

            return cpo;

        }

        #endregion


        #region LiveLinkURL(Session) / StopLinkURL(Session) / LiveLink(Session, EVSEId = TestRoamingNetwork.DCEVSE)

        /// <summary>
        /// Where the live link of the given session is.
        /// </summary>
        public String LiveLinkURL(String Session)
            => $"{URL}/liveLinks/{Session}";

        /// <summary>
        /// Where the stop link of the given session is.
        /// </summary>
        public String StopLinkURL(String Session)
            => $"{URL}/sessions/{Session}/stop";

        /// <summary>
        /// The live link of the given session: of the given EVSE of the DC
        /// station of the test network, followed over "https", asked again
        /// every fifth of a second.
        /// </summary>
        public JObject LiveLink(String   Session,
                                String?  EVSEId = TestRoamingNetwork.DCEVSE)
        {

            var link = new JObject(
                           new JProperty("@context",        "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"),
                           new JProperty("created",         DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")),
                           new JProperty("liveTransports",  new JArray(new JObject(
                               new JProperty("type",     "https"),
                               new JProperty("urls",     new JArray($"{URL}/sessions/{Session}/live")),
                               new JProperty("refresh",  0.2)
                           )))
                       );

            if (EVSEId is not null)
                link.Add(new JProperty("chargingStation", new JObject(
                             new JProperty("@id",   TestRoamingNetwork.DCStation),
                             new JProperty("EVSE",  new JObject(new JProperty("@id", EVSEId)))
                         )));

            return link;

        }

        #endregion

        #region Send(SignedDocument)

        /// <summary>
        /// What the station sends over the live link from now on: a signed
        /// document, e.g. OCMF - or nothing.
        /// </summary>
        public void Send(String? SignedDocument)
        {
            live = SignedDocument;
        }

        #endregion


        #region (private) GetLiveLink(Request) / GetLive(Request) / PostStop(Request)

        private HTTPResponse GetLiveLink(HTTPRequest Request)

            => Response(Request, HTTPStatusCode.OK, HTTPContentType.Application.JSON_UTF8,
                        LiveLink(Request.ParsedURLParameters[0]).ToString());

        private HTTPResponse GetLive(HTTPRequest Request)
        {

            Interlocked.Increment(ref liveRequests);

            return live is String document
                       ? Response(Request, HTTPStatusCode.OK,       HTTPContentType.Text.PLAIN, document)
                       : Response(Request, HTTPStatusCode.NotFound, HTTPContentType.Text.PLAIN, "Nothing yet.");

        }

        private HTTPResponse PostStop(HTTPRequest Request)
        {

            stops.Enqueue((Request.ParsedURLParameters[0], Request.HTTPBodyAsUTF8String ?? ""));

            var answer = StopAnswer;

            return Response(Request, answer, HTTPContentType.Application.JSON_UTF8,
                            new JObject(new JProperty("message", answer.IsSuccessful ? "The station stops." : "This session stops nowhere here.")).ToString());

        }

        private static HTTPResponse Response(HTTPRequest      Request,
                                             HTTPStatusCode   Status,
                                             HTTPContentType  ContentType,
                                             String           Body)

            => new HTTPResponse.Builder(Request) {
                   HTTPStatusCode  = Status,
                   ContentType     = ContentType,
                   Content         = Encoding.UTF8.GetBytes(Body),
                   Connection      = ConnectionType.Close
               };

        #endregion


        public async ValueTask DisposeAsync()
        {
            await server.DisposeAsync();
        }

    }

}
