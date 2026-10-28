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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class SmartphoneHTTPAPI
    {

        #region (private) GetProvider(Request)

        /// <summary>
        /// GET /api/v1/provider: the account at the e-mobility provider -
        /// where, as whom, its account key - without its password and key.
        /// </summary>
        private Task<HTTPResponse> GetProvider(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(SmartphoneAccess.Provider), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, Smartphone.ProviderJSON())
                   );

        }

        #endregion

        #region (private) PostProviderSignUp(Request) / PostProviderSignIn(Request) / PostProviderForget(Request)

        /// <summary>
        /// POST /api/v1/provider/signup with { "url", "username", "email" }: an
        /// account at the e-mobility provider made by the phone, with a
        /// password it makes up and keeps, and an account key of the phone.
        /// </summary>
        private async Task<HTTPResponse> PostProviderSignUp(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Provider), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            return Answer(Request, await Smartphone.SignUpAtProviderAsync(json.Value<String>("url")      ?? "",
                                                                          json.Value<String>("username") ?? "",
                                                                          json.Value<String>("email")    ?? "",
                                                                          user.Id.ToString()));

        }

        /// <summary>
        /// POST /api/v1/provider/signin with { "url", "username", "password" }:
        /// an account the driver has at the e-mobility provider already, taken
        /// by the phone, and an account key of the phone.
        /// </summary>
        private async Task<HTTPResponse> PostProviderSignIn(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Provider), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            return Answer(Request, await Smartphone.SignInAtProviderAsync(json.Value<String>("url")      ?? "",
                                                                          json.Value<String>("username") ?? "",
                                                                          json.Value<String>("password") ?? "",
                                                                          user.Id.ToString()));

        }

        /// <summary>
        /// POST /api/v1/provider/forget: the account forgotten, its account key
        /// taken back at the provider where it can be reached.
        /// </summary>
        private async Task<HTTPResponse> PostProviderForget(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(SmartphoneAccess.Provider), true, out var user, out var refused))
                return refused;

            return Answer(Request, await Smartphone.ForgetProviderAsync(user.Id.ToString()));

        }

        #endregion

        #region (private) Answer(Request, Result)

        /// <summary>
        /// What came of asking the provider: the account as it is now, with what
        /// is to be said about it - or why nothing was done, 400 where the
        /// request was wrong, 502 where the provider could not be reached or
        /// answered nonsense, 500 where the phone could not write it down.
        /// </summary>
        private HTTPResponse Answer(HTTPRequest     Request,
                                    ProviderResult  Result)
        {

            if (!Result.Success)
                return Result.Upstream
                           ? ErrorJSON (Request, HTTPStatusCode.BadGateway, Result.Message)
                           : NotChanged(Request, HTTPStatusCode.BadRequest, Result.Message, Result.NotSaved);

            var json = Smartphone.ProviderJSON();

            json.AddFirst(new JProperty("message", Result.Message));

            return JSONResponse(Request, HTTPStatusCode.OK, json);

        }

        #endregion

    }

}
