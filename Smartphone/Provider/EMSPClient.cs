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

using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// What an e-mobility provider answered: its status code, the JSON of its
    /// answer - or why there is none.
    /// </summary>
    public sealed record EMSPAnswer(Boolean   Success,
                                    Int32     Status,
                                    JObject?  JSON,
                                    String?   Error)
    {

        /// <summary>
        /// What the provider said went wrong, in its own words where it said
        /// any.
        /// </summary>
        public String Problem

            => Error
                   ?? JSON?["error"]?.      Value<String>()
                   ?? JSON?["description"]?.Value<String>()
                   ?? JSON?["message"]?.    Value<String>()
                   ?? $"it answered {Status}.";

    }


    /// <summary>
    /// The phone asking its e-mobility provider - an EMSP of this family -
    /// over its HTTP API: signing up, an account certificate for the phone's
    /// account key, and charging tickets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Signed in the way the provider's own web interface is: with the session
    /// its sign-up or its sign-in (<c>POST /ext/auth/login</c>) hands out, as
    /// cookies, sent with every request after. An account a driver signed up
    /// in a browser has accepted no terms of use, and HTTP Basic is not taken
    /// for it. The session is kept for as long as this client is, and taken
    /// anew where the provider says it is over (401).
    /// </para>
    /// <para>
    /// Names are resolved by the phone's own name servers.
    /// </para>
    /// </remarks>
    public sealed class EMSPClient
    {

        #region Data

        /// <summary>
        /// How long the provider is waited for.
        /// </summary>
        public static readonly TimeSpan  DefaultTimeout  = TimeSpan.FromSeconds(20);

        private readonly Uri            url;
        private readonly DNSClient      dnsClient;
        private readonly TimeSpan       timeout;
        private readonly SemaphoreSlim  signingIn  = new (1, 1);

        private String?  username;
        private String?  password;
        private String?  cookies;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Ask the provider at the given URL.
        /// </summary>
        /// <param name="URL">Where the provider is: its web interface, below which "/ext" and "/api" are.</param>
        /// <param name="DNSClient">How to resolve its name: the phone's own.</param>
        /// <param name="Username">The account to sign in as, where the phone has one there.</param>
        /// <param name="Password">Its password.</param>
        /// <param name="Timeout">How long it is waited for.</param>
        public EMSPClient(String     URL,
                          DNSClient  DNSClient,
                          String?    Username   = null,
                          String?    Password   = null,
                          TimeSpan?  Timeout    = null)
        {
            this.url        = new Uri(URL.TrimEnd('/') + "/");
            this.dnsClient  = DNSClient;
            this.username   = Username;
            this.password   = Password;
            this.timeout    = Timeout ?? DefaultTimeout;
        }

        #endregion


        #region SignUpAsync(Username, EMail, Password, DisplayName)

        /// <summary>
        /// POST /ext/auth/signup: an account at the provider, made by the phone
        /// and signed in at once.
        /// </summary>
        public async Task<EMSPAnswer> SignUpAsync(String  Username,
                                                  String  EMail,
                                                  String  Password,
                                                  String  DisplayName)
        {

            var (answer, setCookies) = await SendAsync(HTTPMethod.POST,
                                                       "ext/auth/signup",
                                                       new JObject(
                                                           new JProperty("username",     Username),
                                                           new JProperty("email",        EMail),
                                                           new JProperty("password",     Password),
                                                           new JProperty("displayName",  DisplayName)
                                                       ),
                                                       null);

            if (answer.Success)
            {
                username  = Username;
                password  = Password;
                cookies   = setCookies;
            }

            return answer;

        }

        #endregion

        #region SignInAsync()

        /// <summary>
        /// POST /ext/auth/login: a session of the account, for what is asked
        /// next.
        /// </summary>
        public async Task<EMSPAnswer> SignInAsync()
        {

            if (username is null || password is null)
                return new EMSPAnswer(false, 0, null, "No account to sign in with.");

            var (answer, setCookies) = await SendAsync(HTTPMethod.POST,
                                                       "ext/auth/login",
                                                       new JObject(
                                                           new JProperty("login",     username),
                                                           new JProperty("password",  password)
                                                       ),
                                                       null);

            cookies = answer.Success ? setCookies : null;

            if (answer.Success && cookies is null)
                return new EMSPAnswer(false, answer.Status, answer.JSON, $"{url} let the account in and handed out no session.");

            return answer;

        }

        #endregion


        #region TicketsAsync()

        /// <summary>
        /// GET /api/v1/tickets: the provider's name for itself in a ticket, its
        /// ticket issuer, and the tickets of the account.
        /// </summary>
        public Task<EMSPAnswer> TicketsAsync()

            => SignedInAsync(HTTPMethod.GET, "api/v1/tickets", null);

        #endregion

        #region AccountKeyAsync(CSR, Label)

        /// <summary>
        /// POST /api/v1/account-keys: an account certificate for the key a
        /// PKCS#10 request is signed with.
        /// </summary>
        public Task<EMSPAnswer> AccountKeyAsync(String  CSR,
                                                String  Label)

            => SignedInAsync(HTTPMethod.POST,
                             "api/v1/account-keys",
                             new JObject(
                                 new JProperty("csr",    CSR),
                                 new JProperty("label",  Label)
                             ));

        #endregion

        #region RevokeAccountKeyAsync(Id)

        /// <summary>
        /// POST /api/v1/account-keys/{id}/revoke: the account key taken back.
        /// </summary>
        public Task<EMSPAnswer> RevokeAccountKeyAsync(String Id)

            => SignedInAsync(HTTPMethod.POST, $"api/v1/account-keys/{Id}/revoke", new JObject());

        #endregion

        #region TicketAsync(Request)

        /// <summary>
        /// POST /api/v1/tickets: a ticket request, answered with the ticket the
        /// provider signed.
        /// </summary>
        public Task<EMSPAnswer> TicketAsync(Byte[] Request)

            => SignedInAsync(HTTPMethod.POST,
                             "api/v1/tickets",
                             new JObject(new JProperty("request", Convert.ToBase64String(Request))));

        #endregion


        #region (private) SignedInAsync(Method, Path, Body)

        /// <summary>
        /// A request with the session of the account: signed in first where
        /// there is none, and once more where the provider says it is over.
        /// </summary>
        private async Task<EMSPAnswer> SignedInAsync(HTTPMethod  Method,
                                                     String      Path,
                                                     JObject?    Body)
        {

            for (var attempt = 0; ; attempt++)
            {

                var session = cookies;

                if (session is null)
                {

                    await signingIn.WaitAsync();

                    try
                    {

                        // Signed in meanwhile by whoever held the semaphore.
                        session = cookies;

                        if (session is null)
                        {

                            var signedIn = await SignInAsync();

                            if (!signedIn.Success)
                                return signedIn;

                            session = cookies;

                        }

                    }
                    finally
                    {
                        signingIn.Release();
                    }

                }

                var (answer, _) = await SendAsync(Method, Path, Body, session);

                if (answer.Status != 401 || attempt > 0)
                    return answer;

                // Over: a new session, once.
                Interlocked.CompareExchange(ref cookies, null, session);

            }

        }

        #endregion

        #region (private) SendAsync(Method, Path, Body, Cookies)

        private async Task<(EMSPAnswer Answer, String? SetCookies)> SendAsync(HTTPMethod  Method,
                                                                              String      Path,
                                                                              JObject?    Body,
                                                                              String?     Cookies)
        {

            try
            {

                var server   = org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse($"{url.Scheme}://{url.Authority}");
                var path     = HTTPPath.Parse(url.AbsolutePath + Path);
                var accept   = AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8);

                Action<HTTPRequest.Builder>? withCookies = Cookies is not null
                                                               ? builder => builder.SetHeaderField("Cookie", Cookies)
                                                               : null;

                await using var http = new HTTPClient(server, DNSClient: dnsClient);

                var response = Method == HTTPMethod.GET
                                   ? await http.GET (path,
                                                     Accept:          accept,
                                                     RequestTimeout:  timeout,
                                                     RequestBuilder:  withCookies)
                                   : await http.POST(path,
                                                     Encoding.UTF8.GetBytes((Body ?? new JObject()).ToString(Formatting.None)),
                                                     HTTPContentType.Application.JSON_UTF8,
                                                     Accept:          accept,
                                                     RequestTimeout:  timeout,
                                                     RequestBuilder:  withCookies);

                var text      = response.HTTPBodyAsUTF8String;
                JObject? json = null;

                if (!String.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        json = JObject.Parse(text);
                    }
                    catch (JsonException)
                    {
                        // Not JSON: what it said is said by its status code.
                    }
                }

                var status = (Int32) response.HTTPStatusCode.Code;

                if (status == 0)
                    return (new EMSPAnswer(false, 0, null, $"{url} could not be reached: {text}"), null);

                return (new EMSPAnswer(response.HTTPStatusCode.IsSuccessful, status, json, null), CookiesOf(response));

            }
            catch (Exception e)
            {
                return (new EMSPAnswer(false, 0, null, $"{url} could not be reached: {e.Message}"), null);
            }

        }

        #endregion

        #region (private static) CookiesOf(Response)

        /// <summary>
        /// The cookies a response sets, as a request sends them back:
        /// "name=value; name=value" - without their attributes.
        /// </summary>
        private static String? CookiesOf(HTTPResponse Response)
        {

            var lines = Response.GetHeaderField<String[]>("Set-Cookie")
                            ?? (Response.GetHeaderField<String>("Set-Cookie") is String one ? [ one ] : []);

            var pairs = lines.Select(line => line.Split(';', 2)[0].Trim()).
                              Where (pair => pair.Contains('=') && !pair.EndsWith('=')).
                              ToArray();

            return pairs.Length > 0
                       ? String.Join("; ", pairs)
                       : null;

        }

        #endregion

    }

}
