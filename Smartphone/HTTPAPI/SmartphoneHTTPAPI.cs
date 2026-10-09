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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// The JSON API the browser talks to, registered at "/api": what every
    /// node has - see <see cref="NodeHTTPAPI"/> - and the smartphone's map and
    /// charging sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sign-in, the status and the clock, the configuration, name
    /// resolution and the time servers, the certificate store, the log and the
    /// event stream are the node's, as they are every other kind's.
    /// </para>
    /// <para>
    /// | | |
    /// |---|---|
    /// | <c>GET v1/map</c> | the charging stations, OCPI locations with the status of each EVSE now |
    /// | <c>GET v1/configuration/map</c>, <c>PUT</c> | where the map takes its stations from: <c>{ "stationsURL" }</c> |
    /// | <c>GET v1/charging/sessions</c> | every session, the newest first; <c>?active=true</c> for those still charging |
    /// | <c>POST v1/charging/sessions</c> | <c>{ "locationId", "evseUid" }</c>: start one, 201 and the session |
    /// | <c>GET v1/charging/sessions/{id}</c> | one session, as it is now |
    /// | <c>POST v1/charging/sessions/{id}/stop</c> | stop one that is still charging |
    /// </para>
    /// <para>
    /// No event of its own on the node's stream: that stream is for whoever
    /// may read the log, and a driver need not. The pages ask again instead,
    /// every few seconds while a session is open.
    /// </para>
    /// </remarks>
    public partial class SmartphoneHTTPAPI : NodeHTTPAPI
    {

        #region Properties

        /// <summary>
        /// The smartphone this API speaks for.
        /// </summary>
        public Smartphone  Smartphone  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create and register the JSON API within the given HTTP server.
        /// </summary>
        /// <param name="HTTPServer">The HTTP server.</param>
        /// <param name="Smartphone">The smartphone this API speaks for.</param>
        /// <param name="ExtAPI">The accounts and the groups they are in.</param>
        /// <param name="Log">Everything that happens inside this smartphone.</param>
        /// <param name="APIPath">The root path of the API, the smartphone's own by default.</param>
        /// <param name="Version">The version reported by the status resource.</param>
        public SmartphoneHTTPAPI(HTTPServer  HTTPServer,
                                 Smartphone  Smartphone,
                                 HTTPExtAPI  ExtAPI,
                                 EventLog    Log,
                                 HTTPPath?   APIPath   = null,
                                 String?     Version   = null)

            : base(HTTPServer,
                   Smartphone,
                   ExtAPI,
                   Log,
                   APIPath,
                   Version ?? typeof(SmartphoneHTTPAPI).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")

        {

            this.Smartphone = Smartphone;

            RegisterURLTemplates();

        }

        #endregion


        #region (private) RegisterURLTemplates()

        /// <summary>
        /// What only a smartphone has.
        /// </summary>
        private void RegisterURLTemplates()
        {

            AddHandler(HTTPPath.Root + "v1/map",                              GetMap,                    HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/configuration/map",                GetMapConfiguration,       HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/configuration/map",                PutMapConfiguration,       HTTPMethod.PUT);

            AddHandler(HTTPPath.Root + "v1/charging/sessions",                GetChargingSessions,       HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/charging/sessions",                PostChargingSession,       HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/charging/sessions/{id}",           GetChargingSession,        HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/charging/sessions/{id}/stop",      PostStopChargingSession,   HTTPMethod.POST);

        }

        #endregion

    }

}
