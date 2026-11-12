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

using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Web;

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Provider;

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
        /// PUT /api/v1/configuration/roamingNetwork with { "trustAnchor",
        /// "trustedKeys"? }: the text of its QR code, its URI or its JSON - or
        /// empty, and the map shows its demo stations - and the thumbprints of
        /// those of its identity keys to trust. Without them, the keys trusted
        /// so far stay trusted where the anchor stays the same, and none is
        /// where it changed. Answers with what it says now.
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

        #region (private) GetRoamingNetworkKeyStatement(Request)

        /// <summary>
        /// GET /api/v1/configuration/roamingNetwork/keyStatement: the key
        /// statement of the operator of the roaming network, asked of it now -
        /// its identity keys, who holds them, whether it is signed validly -
        /// and whether the keys trusted here are a quorum of it. 200 also where
        /// the network did not answer, with why; 409 without a trust anchor.
        /// </summary>
        private async Task<HTTPResponse> GetRoamingNetworkKeyStatement(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Map), false, out _, out var refused))
                return refused;

            var (check, error) = await Smartphone.CheckKeyStatementAsync();

            if (check is null)
                return ErrorJSON(Request, HTTPStatusCode.Conflict, error!);

            return JSONResponse(Request, HTTPStatusCode.OK, check.ToJSON());

        }

        #endregion

        #region (private) PutTrustedKey(Request) / DeleteTrustedKey(Request)

        /// <summary>
        /// PUT /api/v1/configuration/roamingNetwork/trustedKeys/{id}: trust one
        /// identity key of the trust anchor, by its thumbprint. Answers with what
        /// the configuration says now; 404 where the trust anchor names no such
        /// key, or there is none.
        /// </summary>
        private Task<HTTPResponse> PutTrustedKey(HTTPRequest Request)

            => SetKeyTrust(Request, true);

        /// <summary>
        /// DELETE /api/v1/configuration/roamingNetwork/trustedKeys/{id}: no
        /// longer trust one identity key of the trust anchor.
        /// </summary>
        private Task<HTTPResponse> DeleteTrustedKey(HTTPRequest Request)

            => SetKeyTrust(Request, false);

        private Task<HTTPResponse> SetKeyTrust(HTTPRequest  Request,
                                               Boolean      Trusted)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Map), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!Smartphone.TrySetKeyTrust(IdOf(Request), Trusted, user.Id.ToString(), out var error, out var notFound, out var notSaved))
                return Task.FromResult(NotChanged(Request, notFound ? HTTPStatusCode.NotFound : HTTPStatusCode.BadRequest, error, notSaved));

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
        /// the newest first - "?active=true" for those still starting or
        /// charging. Those started through the e-mobility provider as it
        /// reports them now.
        /// </summary>
        private async Task<HTTPResponse> GetChargingSessions(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Charging), false, out _, out var refused))
                return refused;

            var onlyActive = String.Equals(Request.QueryString.GetString("active"), "true", StringComparison.OrdinalIgnoreCase);

            await Smartphone.ReportsFromProviderAsync(Smartphone.Sessions.All());

            return JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionsJSON(onlyActive));

        }

        /// <summary>
        /// GET /api/v1/charging/sessions/{id}: one charging session, as it is
        /// now - as the e-mobility provider reports it, where it was started
        /// through one.
        /// </summary>
        private async Task<HTTPResponse> GetChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Charging), false, out _, out var refused))
                return refused;

            var id       = IdOf(Request);
            var session  = Smartphone.Sessions.Get(id);

            if (session is null)
                return ErrorJSON(Request, HTTPStatusCode.NotFound, $"There is no charging session '{id}' on this phone.");

            await Smartphone.ReportsFromProviderAsync([ session ]);

            return JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionJSON(Smartphone.Sessions.Get(id) ?? session, Smartphone.TimeProvider.GetUtcNow()));

        }

        #endregion

        #region (private) PostChargingSession(Request) / PostStopChargingSession(Request)

        /// <summary>
        /// POST /api/v1/charging/sessions with { "locationId", "evseUid" }:
        /// start charging there - simulated, or through the e-mobility provider
        /// with a charging ticket of its own where the phone has one. 201 with
        /// the session; 404 where the station or its EVSE is not on the map;
        /// 409 where the EVSE is not available; 502 where the provider issued
        /// no ticket or started nothing. Through the provider with a "limit",
        /// { "kWh", "minutes" }, the limits of its charging ticket, of the few
        /// values everybody chooses among: 400 for any other, 409 where the
        /// phone has no provider. With a "liveLink" as well - the https
        /// URL of the live link the web payment page of the operator gave, or
        /// its document - the session that page started there, followed over
        /// its live link: 400 where it is none, 409 where it is of another EVSE
        /// or the proof of the station does not hold, 502 where its URL could
        /// not be fetched. With the "stopLink" the page gave as well, where it
        /// gave one - the https URL a POST stops the session at -, its
        /// "disputeLink" - the https URL a POST disputes it at -, its
        /// "paymentReference" - the reference of the payment it took, which
        /// every signed value of the session is to name as its identification
        /// ("ID") - the "limit" the driver chose there, as OCPP 2.1 says it
        /// to the station: { "maxEnergy" in Wh, "maxCost", "maxTime" in
        /// seconds, "maxSoC" in percent } - and its "paymentLink" - the https
        /// URL that says the price agreement and the receipt of the session;
        /// 400 where one is none. Or, in place
        /// of all of these, the "handOver" the page gave - the https URL of
        /// the document that names them, taken from the host of the web
        /// payment page the proof of the station names alone, or the document
        /// itself: 400 where it is none, or comes with one of them as well,
        /// 409 where it is at another host or the proof names no web payment
        /// page, 502 where it could not be fetched.
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

            if (json["handOver"] is JToken handOver && handOver.Type != JTokenType.Null)
            {

                if (handOver.Type is not (JTokenType.String or JTokenType.Object))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                     "'handOver' is the URL of the hand-over the web payment page of the operator gave, or its document.");

                if (HandedOverAlone.FirstOrDefault(name => json[name] is JToken given && given.Type != JTokenType.Null) is String also)
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                     $"A hand-over names the live link, the links, the payment reference and the limit the web payment page gave: '{also}' is not handed over beside it.");

                var takenOver = await Smartphone.TakeHandOverAsync(locationId, evseUid, handOver, user.Id.ToString());

                if (takenOver.Session is null)
                    return Refused(Request, takenOver.Refusal, takenOver.Error ?? "No hand-over is taken.");

                return JSONResponse(Request, HTTPStatusCode.Created, Smartphone.SessionJSON(takenOver.Session, Smartphone.TimeProvider.GetUtcNow()));

            }

            if (json["liveLink"] is JToken liveLink && liveLink.Type != JTokenType.Null)
            {

                if (liveLink.Type is not (JTokenType.String or JTokenType.Object))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                     "'liveLink' is the URL of the live link the web payment page of the operator gave, or its document.");

                if (!TryText(json, "stopLink", StopLinkIs, out var stopLink, out var stopLinkError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, stopLinkError);

                if (!TryText(json, "disputeLink", DisputeLinkIs, out var disputeLink, out var disputeLinkError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, disputeLinkError);

                if (!TryText(json, "paymentReference", PaymentReferenceIs, out var paymentReference, out var paymentReferenceError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, paymentReferenceError);

                if (!TryText(json, "paymentLink", PaymentLinkIs, out var paymentLink, out var paymentLinkError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, paymentLinkError);

                if (!TransactionLimit.TryParse(json["limit"], out var limit, out var limitError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, limitError);

                var followed = await Smartphone.FollowLiveLinkAsync(locationId, evseUid, liveLink, user.Id.ToString(), stopLink, limit, disputeLink, paymentReference, paymentLink);

                if (followed.Session is null)
                    return Refused(Request, followed.Refusal, followed.Error ?? "No live link is followed.");

                return JSONResponse(Request, HTTPStatusCode.Created, Smartphone.SessionJSON(followed.Session, Smartphone.TimeProvider.GetUtcNow()));

            }

            // The limits of the charging ticket of this start, of the few
            // values everybody chooses among.
            if (!TicketLimits.TryParse(json["limit"], out var limits, out var limitsError))
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, limitsError);

            var start = await Smartphone.StartChargingAsync(locationId, evseUid, user.Id.ToString(), limits);

            if (start.Session is null)
                return Refused(Request, start.Refusal, start.Error ?? "Nothing was started.");

            return JSONResponse(Request, HTTPStatusCode.Created, Smartphone.SessionJSON(start.Session, Smartphone.TimeProvider.GetUtcNow()));

        }

        /// <summary>
        /// POST /api/v1/charging/sessions/{id}/stop: stop a session that is
        /// still charging - through the e-mobility provider where it was
        /// started through it, and through the stop link of the web payment
        /// page where that page started it: the one handed over with its live
        /// link, or { "stopLink" } now. 409 where it is over already, or the
        /// page gave no stop link; 502 where the provider or the page stopped
        /// nothing, and the session goes on.
        /// </summary>
        private async Task<HTTPResponse> PostStopChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return refused;

            String? stopLink = null;

            if (!String.IsNullOrWhiteSpace(Request.HTTPBodyAsUTF8String))
            {

                if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                    return errorResponse;

                if (!TryText(json, "stopLink", StopLinkIs, out stopLink, out var stopLinkError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, stopLinkError);

            }

            var stop = await Smartphone.StopChargingAsync(IdOf(Request), user.Id.ToString(), stopLink);

            if (stop.Session is null)
                return Refused(Request, stop.Refusal, stop.Error ?? "Nothing was stopped.");

            return JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionJSON(stop.Session, Smartphone.TimeProvider.GetUtcNow()));

        }

        #endregion

        #region (private) PostDisputeChargingSession(Request)

        /// <summary>
        /// POST /api/v1/charging/sessions/{id}/dispute: dispute a session the
        /// web payment page of the operator of its station started, through
        /// the dispute link the page gave - the one handed over with its live
        /// link, or { "disputeLink" } now -, for why this phone disputes it:
        /// one of its signed values is a wrong one, or none signed its end.
        /// This phone sends that itself once it knows: asked here, it sends it
        /// again. 409 where nothing of it is disputed, it was disputed already,
        /// or the page gave no dispute link; 502 where the page took nothing.
        /// </summary>
        private async Task<HTTPResponse> PostDisputeChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return refused;

            String? disputeLink = null;

            if (!String.IsNullOrWhiteSpace(Request.HTTPBodyAsUTF8String))
            {

                if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                    return errorResponse;

                if (!TryText(json, "disputeLink", DisputeLinkIs, out disputeLink, out var disputeLinkError))
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, disputeLinkError);

            }

            var dispute = await Smartphone.DisputeChargingAsync(IdOf(Request), user.Id.ToString(), disputeLink);

            if (dispute.Session is null)
                return Refused(Request, dispute.Refusal, dispute.Error ?? "Nothing was disputed.");

            return JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionJSON(dispute.Session, Smartphone.TimeProvider.GetUtcNow()));

        }

        #endregion

        #region (private) PostPaymentChargingSession(Request)

        /// <summary>
        /// POST /api/v1/charging/sessions/{id}/payment: ask the payment link the
        /// web payment page of the operator of its station gave for a session
        /// now - its price agreement and its receipt, where they did not come
        /// yet -, each checked as it can be, and the session disputed where one
        /// does not hold. This phone asks it itself until both came: asked
        /// here, it asks at once. 409 where the page gave no payment link.
        /// </summary>
        private async Task<HTTPResponse> PostPaymentChargingSession(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(SmartphoneAccess.Charging), true, out var user, out var refused))
                return refused;

            var asked = await Smartphone.AskPaymentNowAsync(IdOf(Request), user.Id.ToString());

            if (asked.Session is null)
                return Refused(Request, asked.Refusal, asked.Error ?? "Nothing was asked.");

            return JSONResponse(Request, HTTPStatusCode.OK, Smartphone.SessionJSON(asked.Session, Smartphone.TimeProvider.GetUtcNow()));

        }

        #endregion


        #region (private static) TryText(JSON, Name, What, out Text, out Error)

        private const String StopLinkIs          = "the https URL the web payment page of the operator gave to stop the session with";
        private const String DisputeLinkIs       = "the https URL the web payment page of the operator gave to dispute the session with";
        private const String PaymentReferenceIs  = "the reference of the payment the web payment page of the operator took, as the station writes it into the identification (\"ID\") of the signed meter values of the session";
        private const String PaymentLinkIs       = "the https URL the web payment page of the operator gave to ask the payment of the session at";

        /// <summary>
        /// What a hand-over names, and is not handed over beside it.
        /// </summary>
        private static readonly String[] HandedOverAlone = [ "liveLink", "stopLink", "disputeLink", "paymentReference", "limit", "paymentLink" ];

        /// <summary>
        /// What of a request the web payment page gave, where it names it -
        /// its "stopLink", its "disputeLink" or its "paymentReference": a
        /// text. Whether it is one, the phone says.
        /// </summary>
        private static Boolean TryText(JObject                           JSON,
                                       String                            Name,
                                       String                            What,
                                       out String?                       Text,
                                       [NotNullWhen(false)] out String?  Error)
        {

            Text   = null;
            Error  = null;

            switch (JSON[Name])
            {

                case null or { Type: JTokenType.Null }:
                    return true;

                case JValue { Type: JTokenType.String } value when value.Value<String>() is String text:
                    Text = String.IsNullOrWhiteSpace(text) ? null : text.Trim();
                    return true;

                default:
                    Error = $"'{Name}' is {What}.";
                    return false;

            }

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
                             ChargingRefusal.NotFound     => HTTPStatusCode.NotFound,
                             ChargingRefusal.NotSaved     => HTTPStatusCode.InternalServerError,
                             ChargingRefusal.Provider     => HTTPStatusCode.BadGateway,
                             ChargingRefusal.Unreachable  => HTTPStatusCode.BadGateway,
                             ChargingRefusal.Operator     => HTTPStatusCode.BadGateway,
                             ChargingRefusal.Invalid      => HTTPStatusCode.BadRequest,
                             _                            => HTTPStatusCode.Conflict
                         },
                         Error);

        #endregion

    }

}
