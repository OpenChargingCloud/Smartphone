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

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// What the web payment page of the operator of a station gives the driver
    /// for the session it started there, in one document the phone reads: the
    /// live link of the session, its stop link and its dispute link, the
    /// reference of the payment it took and the limit the driver chose - all
    /// of it at once, not each typed or pasted on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page gives it at an https URL of this session alone. The phone
    /// takes it from the host of the web payment page the proof of the station
    /// names, and from no other: what it says is then what that page says,
    /// over TLS to a host the operator signed. Its links are bound to the
    /// operator where they are at that host as well, and its payment reference
    /// is bound to it, since it came from there.
    /// </para>
    /// <para>
    /// It is read as the roaming network writes what it signs: an "@context"
    /// that says what it is, and the names below - others are passed over, so
    /// that a page may say more than the phone reads.
    /// </para>
    /// </remarks>
    /// <param name="LiveLink">The live link of the session: the https URL of its document, or the document itself.</param>
    /// <param name="StopLink">The https URL of this session alone a POST stops it at, where the page gives one.</param>
    /// <param name="DisputeLink">The https URL of this session alone a POST disputes it at, where the page gives one.</param>
    /// <param name="PaymentReference">The reference of the payment the page took, which the station writes into the identification ("ID") of every signed value of the session, where the page gives one.</param>
    /// <param name="Limit">The limit the driver chose at the page, as OCPP 2.1 says it to the station, where the driver chose one.</param>
    public sealed record WebPaymentHandOver(JToken             LiveLink,
                                            String?            StopLink          = null,
                                            String?            DisputeLink       = null,
                                            String?            PaymentReference  = null,
                                            TransactionLimit?  Limit             = null)
    {

        #region Data

        /// <summary>
        /// What a hand-over names as its "@context".
        /// </summary>
        public const String  JSONLDContext              = "https://open.charging.cloud/contexts/wwcp/webPayment/handOver/v1";

        /// <summary>
        /// How long a payment reference is at most.
        /// </summary>
        public const Int32   MaxPaymentReferenceLength  = 255;

        #endregion


        #region ToJSON()

        /// <summary>
        /// The hand-over as the page gives it.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("@context",  JSONLDContext),
                           new JProperty("liveLink",  LiveLink.DeepClone())
                       );

            if (StopLink         is not null)  json.Add(new JProperty("stopLink",          StopLink));
            if (DisputeLink      is not null)  json.Add(new JProperty("disputeLink",       DisputeLink));
            if (PaymentReference is not null)  json.Add(new JProperty("paymentReference",  PaymentReference));
            if (Limit            is not null)  json.Add(new JProperty("limit",             Limit.ToJSON()));

            return json;

        }

        #endregion

        #region TryParse(JSON, out HandOver, out Error)

        /// <summary>
        /// The hand-over a page gave, or why what it gave is none.
        /// </summary>
        public static Boolean TryParse(JObject                                       JSON,
                                       [NotNullWhen(true)]  out WebPaymentHandOver?  HandOver,
                                       [NotNullWhen(false)] out String?              Error)
        {

            HandOver  = null;
            Error     = null;

            if (JSON["@context"] is not JValue { Type: JTokenType.String } context || context.Value<String>() != JSONLDContext)
            {
                Error = $"It is no hand-over of a web payment page: that names the @context '{JSONLDContext}'.";
                return false;
            }

            var liveLink = JSON["liveLink"];

            if (liveLink is not (JObject or JValue { Type: JTokenType.String }) ||
                liveLink is JValue text && String.IsNullOrWhiteSpace(text.Value<String>()))
            {
                Error = "Its 'liveLink' is the https URL of the live link of the session, or its document.";
                return false;
            }

            if (!TryText(JSON, "stopLink",         out var stopLink,         out Error) ||
                !TryText(JSON, "disputeLink",      out var disputeLink,      out Error) ||
                !TryText(JSON, "paymentReference", out var paymentReference, out Error))
            {
                return false;
            }

            if (stopLink is not null && !TryLinkURL(stopLink, out _))
            {
                Error = $"Its 'stopLink' '{stopLink}' is no https URL to stop the session with.";
                return false;
            }

            if (disputeLink is not null && !TryLinkURL(disputeLink, out _))
            {
                Error = $"Its 'disputeLink' '{disputeLink}' is no https URL to dispute the session with.";
                return false;
            }

            if (paymentReference is not null && !IsPaymentReference(paymentReference))
            {
                Error = $"Its 'paymentReference' '{paymentReference}' is no payment reference: a text of at most {MaxPaymentReferenceLength} characters, none of them a control character.";
                return false;
            }

            if (!TransactionLimit.TryParse(JSON["limit"], out var limit, out var limitError))
            {
                Error = $"Its {limitError}";
                return false;
            }

            HandOver = new WebPaymentHandOver(liveLink.DeepClone(), stopLink, disputeLink, paymentReference, limit);
            return true;

        }

        private static Boolean TryText(JObject                           JSON,
                                       String                            Name,
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
                    Error = $"Its '{Name}' is a text.";
                    return false;

            }

        }

        #endregion


        #region (internal static) TryLinkURL(Text, out URI) / IsPaymentReference(Text) / HostOf(URL)

        /// <summary>
        /// Whether the given text is a link the web payment page may give - of
        /// a hand-over or a live link, or to stop or dispute a session with: an
        /// https URL, or an http URL on this machine's loopback.
        /// </summary>
        internal static Boolean TryLinkURL(String                         Text,
                                           [NotNullWhen(true)] out Uri?   URI)

            => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out URI) &&
               (URI.Scheme == Uri.UriSchemeHttps || (URI.Scheme == Uri.UriSchemeHttp && URI.IsLoopback));

        /// <summary>
        /// Whether the given text is a reference of a payment the web payment
        /// page may give, as the station writes it into the identification
        /// ("ID") of an OCMF document: a text, no control character in it.
        /// </summary>
        internal static Boolean IsPaymentReference(String Text)

            => Text.Length is > 0 and <= MaxPaymentReferenceLength &&
               !Text.Any(Char.IsControl);

        /// <summary>
        /// The host of a link, with its port where it is not the one of its
        /// scheme - what the pages and the log show of it, and what links are
        /// compared by - or null where it is no URL.
        /// </summary>
        internal static String? HostOf(String? URL)

            => URL is not null && Uri.TryCreate(URL, UriKind.Absolute, out var uri)
                   ? uri.Authority
                   : null;

        #endregion

    }

}
