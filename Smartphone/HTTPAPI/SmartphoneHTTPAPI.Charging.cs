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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class SmartphoneHTTPAPI
    {

        #region (private) GetMap(Request)

        /// <summary>
        /// GET /api/v1/map: every charging station of the map, with the status
        /// of each EVSE now - "?refresh=true" to ask the roaming network again
        /// rather than show what it listed within the last minute.
        /// </summary>
        private async Task<HTTPResponse> GetMap(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return refused;

            var refresh = String.Equals(Request.QueryString.GetString("refresh"), "true", StringComparison.OrdinalIgnoreCase);

            return JSONResponse(Request, HTTPStatusCode.OK, await Smartphone.LocationsJSONAsync(refresh));

        }

        #endregion

        #region (private) GetLocationCheck(Request)

        /// <summary>
        /// GET /api/v1/map/locations/{id}/check: the charging stations of a pool
        /// of the roaming network checked now - the proof of each against the
        /// trust anchor, and their price statements - and everything a proof
        /// that holds gives: the tariffs, and the energy meters with the public
        /// keys they sign their measurements with. 200 with whether they hold;
        /// 404 where the map has no such pool; 409 where the map shows its demo
        /// stations, which nobody signed.
        /// </summary>
        private async Task<HTTPResponse> GetLocationCheck(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return refused;

            var (check, error, notFound) = await Smartphone.CheckLocationAsync(IdOf(Request));

            if (check is null)
                return ErrorJSON(Request, notFound ? HTTPStatusCode.NotFound : HTTPStatusCode.Conflict, error!);

            return JSONResponse(Request, HTTPStatusCode.OK, check.ToJSON());

        }

        #endregion

        #region (private) GetRoamingNetworkConfiguration(Request) / PutRoamingNetworkConfiguration(Request)

        /// <summary>
        /// GET /api/v1/configuration/roamingNetwork: the roaming network the map
        /// takes its stations from, as its trust anchor says it.
        /// </summary>
        private Task<HTTPResponse> GetRoamingNetworkConfiguration(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.RoamingNetworkConfigurationJSON())
                   );

        }

        /// <summary>
        /// PUT /api/v1/configuration/roamingNetwork with { "trustAnchor" }: the
        /// text of its QR code, its URI or its JSON - or empty, and the map
        /// shows its demo stations. Answers with what it says now.
        /// </summary>
        private Task<HTTPResponse> PutRoamingNetworkConfiguration(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Map), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!Smartphone.TryUpdateRoamingNetworkConfiguration(json, user.Id.ToString(), out var error, out var notSaved))
                return Task.FromResult(NotChanged(Request, HTTPStatusCode.BadRequest, error, notSaved));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.RoamingNetworkConfigurationJSON())
                   );

        }

        #endregion

        #region (private) GetMapConfiguration(Request) / PutMapConfiguration(Request)

        /// <summary>
        /// GET /api/v1/configuration/map: where the map takes its stations from.
        /// </summary>
        private Task<HTTPResponse> GetMapConfiguration(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.MapConfigurationJSON())
                   );

        }

        /// <summary>
        /// PUT /api/v1/configuration/map with { "stationsURL" }: point the map
        /// at another source - or at none, with an empty one. Answers with
        /// what it says now.
        /// </summary>
        private Task<HTTPResponse> PutMapConfiguration(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Map), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!Smartphone.TryUpdateMapConfiguration(json, user.Id.ToString(), out var error, out var notSaved))
                return Task.FromResult(NotChanged(Request, HTTPStatusCode.BadRequest, error, notSaved));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.MapConfigurationJSON())
                   );

        }

        #endregion


        #region (private) GetChargingSessions(Request) / GetChargingSession(Request)

        /// <summary>
        /// GET /api/v1/charging/sessions: every charging session of this phone,
        /// the newest first - "?active=true" for those still charging.
        /// </summary>
        private Task<HTTPResponse> GetChargingSessions(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Charging), false, out _, out var refused))
                return Task.FromResult(refused);

            var onlyActive = String.Equals(Request.QueryString.GetString("active"), "true", StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionsJSON(onlyActive))
                   );

        }

        /// <summary>
        /// GET /api/v1/charging/sessions/{id}: one charging session, as it is
        /// now.
        /// </summary>
        private Task<HTTPResponse> GetChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Charging), false, out _, out var refused))
                return Task.FromResult(refused);

            var id       = IdOf(Request);
            var session  = Smartphone.Sessions.Get(id);

            if (session is null)
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.NotFound, $"There is no charging session '{id}' on this phone."));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, session.ToJSON(Smartphone.TimeProvider.GetUtcNow()))
                   );

        }

        #endregion

        #region (private) PostChargingSession(Request) / PostStopChargingSession(Request)

        /// <summary>
        /// POST /api/v1/charging/sessions with { "locationId", "evseUid" }:
        /// start charging there - simulated, or through the e-mobility provider
        /// with a charging ticket of its own where the phone has one. 201 with
        /// the session; 404 where the station or its EVSE is not on the map;
        /// 409 where the EVSE is not available; 502 where the provider issued
        /// no ticket or started nothing.
        /// </summary>
        private async Task<HTTPResponse> PostChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            var locationId  = json.Value<String>("locationId")?.Trim();
            var evseUid     = json.Value<String>("evseUid")?.   Trim();

            if (String.IsNullOrEmpty(locationId) || String.IsNullOrEmpty(evseUid))
                return ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                 "Say where to charge: 'locationId' and 'evseUid', as the map gives them.");

            var start = await Smartphone.StartChargingAsync(locationId, evseUid, user.Id.ToString());

            if (start.Session is null)
                return Refused(Request, start.Refusal, start.Error ?? "Nothing was started.");

            return JSONResponse(Request, HTTPStatusCode.Created, start.Session.ToJSON(Smartphone.TimeProvider.GetUtcNow()));

        }

        /// <summary>
        /// POST /api/v1/charging/sessions/{id}/stop: stop a session that is
        /// still charging - through the e-mobility provider where it was
        /// started through it. 409 where it is over already; 502 where the
        /// provider stopped nothing, and the session goes on.
        /// </summary>
        private async Task<HTTPResponse> PostStopChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return refused;

            var stop = await Smartphone.StopChargingAsync(IdOf(Request), user.Id.ToString());

            if (stop.Session is null)
                return Refused(Request, stop.Refusal, stop.Error ?? "Nothing was stopped.");

            return JSONResponse(Request, HTTPStatusCode.OK, stop.Session.ToJSON(Smartphone.TimeProvider.GetUtcNow()));

        }

        #endregion


        #region (private static) IdOf(Request) / Refused(Request, Refusal, Error)

        /// <summary>
        /// The id in the path.
        /// </summary>
        private static String IdOf(HTTPRequest Request)

            => Request.ParsedURLParameters.Length > 0
                   ? Request.ParsedURLParameters[0].Trim()
                   : "";

        /// <summary>
        /// A no, with the status code its kind has.
        /// </summary>
        private static HTTPResponse Refused(HTTPRequest      Request,
                                            ChargingRefusal  Refusal,
                                            String           Error)

            => ErrorJSON(Request,
                         Refusal switch {
                             ChargingRefusal.NotFound  => HTTPStatusCode.NotFound,
                             ChargingRefusal.NotSaved  => HTTPStatusCode.InternalServerError,
                             ChargingRefusal.Provider  => HTTPStatusCode.BadGateway,
                             _                         => HTTPStatusCode.Conflict
                         },
                         Error);

        #endregion

    }

}
