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
        /// of each EVSE now.
        /// </summary>
        private Task<HTTPResponse> GetMap(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.LocationsJSON())
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
        /// start charging there - simulated. 201 with the session; 404 where
        /// the station or its EVSE is not on the map; 409 where the EVSE is not
        /// available.
        /// </summary>
        private Task<HTTPResponse> PostChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            var locationId  = json.Value<String>("locationId")?.Trim();
            var evseUid     = json.Value<String>("evseUid")?.   Trim();

            if (String.IsNullOrEmpty(locationId) || String.IsNullOrEmpty(evseUid))
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                                 "Say where to charge: 'locationId' and 'evseUid', as the map gives them."));

            if (!Smartphone.TryStartCharging(locationId, evseUid, user.Id.ToString(), out var session, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.Created, session.ToJSON(Smartphone.TimeProvider.GetUtcNow()))
                   );

        }

        /// <summary>
        /// POST /api/v1/charging/sessions/{id}/stop: stop a session that is
        /// still charging. 409 where it is over already.
        /// </summary>
        private Task<HTTPResponse> PostStopChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!Smartphone.TryStopCharging(IdOf(Request), user.Id.ToString(), out var session, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, session.ToJSON(Smartphone.TimeProvider.GetUtcNow()))
                   );

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
                             _                         => HTTPStatusCode.Conflict
                         },
                         Error);

        #endregion

    }

}
