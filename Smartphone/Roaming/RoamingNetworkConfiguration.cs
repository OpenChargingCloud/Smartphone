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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// The roaming network the map takes its charging stations from: the
    /// "roamingNetwork" section of the configuration file, which holds its
    /// trust anchor - or none, and the map shows its demo stations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trust anchor is what an app pins of a roaming network (WWCP_POI's
    /// SPECIFICATION.md, section 8.6): where the network is served, one URL or
    /// more, and the thumbprints of the identity keys of its operator, with how
    /// many of their holders must agree. It is what the operator prints as a QR
    /// code, on its website or in its documents. Nothing else is trusted: not
    /// the servers, which only pass on what is signed, and not a key a proof
    /// names of itself. A QR code is as trustworthy as the place it was found:
    /// one stuck on a charging station may be anybody's.
    /// </para>
    /// <para>
    /// It is taken as the text of its QR code, <c>WWCP/TA-2:</c> and the
    /// Base45 of its CBOR, as its URI, or as its JSON, and kept as its JSON,
    /// which a person can read:
    /// <code>
    /// "roamingNetwork": { "trustAnchor": { "@id": "QA", "urls": [ "https://hub.example/poi/RNs/QA" ], "keys": [ "a1…" ] } }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="TrustAnchor">The trust anchor of the roaming network, or null for none.</param>
    public sealed record RoamingNetworkConfiguration(RoamingNetworkTrustAnchor? TrustAnchor)
    {

        #region Data

        /// <summary>
        /// The name of the section in the configuration file.
        /// </summary>
        public const String  SectionName     = "roamingNetwork";

        /// <summary>
        /// The longest text of a trust anchor taken: a QR code holds less than
        /// three kilobytes, and longer is somebody who pasted the wrong thing.
        /// </summary>
        public const Int32   MaxTextLength   = 8192;

        /// <summary>
        /// What a smartphone starts with: no roaming network.
        /// </summary>
        public static readonly RoamingNetworkConfiguration Default = new ((RoamingNetworkTrustAnchor?) null);

        #endregion

        #region Properties

        /// <summary>
        /// Whether a roaming network is set.
        /// </summary>
        [MemberNotNullWhen(true, nameof(TrustAnchor))]
        public Boolean IsSet
            => TrustAnchor is not null;

        #endregion


        #region FromDocument(Document, out Configuration, out Error)

        /// <summary>
        /// The "roamingNetwork" section of the whole configuration document -
        /// or the default, where it has none.
        /// </summary>
        public static Boolean FromDocument(JObject                                                Document,
                                           [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                           [NotNullWhen(false)] out String?                       Error)
        {

            if (!Document.TryGetValue(SectionName, out var section) || section.Type == JTokenType.Null)
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            if (section is not JObject json)
            {
                Configuration  = null;
                Error          = $"The \"{SectionName}\" section of the configuration file is no JSON object.";
                return false;
            }

            return TryParse(json, out Configuration, out Error);

        }

        #endregion

        #region TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// A "roamingNetwork" section, or what a page sent in its shape: its
        /// "trustAnchor" as text - of a QR code, a URI or JSON - or as JSON;
        /// empty or absent for none.
        /// </summary>
        public static Boolean TryParse(JObject                                                JSON,
                                       [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                       Error)
        {

            Configuration = null;

            var token = JSON["trustAnchor"];

            if (token is null || token.Type == JTokenType.Null ||
               (token.Type == JTokenType.String && token.Value<String>()!.Trim().Length == 0))
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            RoamingNetworkTrustAnchor? trustAnchor;

            if (token is JObject anchorJSON)
            {
                if (!RoamingNetworkTrustAnchor.TryParse(anchorJSON, out trustAnchor, out var anchorError))
                {
                    Error = "That is no trust anchor: " + anchorError;
                    return false;
                }
            }

            else if (token.Type == JTokenType.String)
            {
                if (!TryParseText(token.Value<String>()!, out trustAnchor, out Error))
                    return false;
            }

            else
            {
                Error = "'trustAnchor' is the text of its QR code, its URI or its JSON - or empty for none.";
                return false;
            }

            Configuration  = new RoamingNetworkConfiguration(trustAnchor);
            Error          = null;
            return true;

        }

        #endregion

        #region TryParseText(Text, out TrustAnchor, out Error)

        /// <summary>
        /// A trust anchor as it is pasted or scanned: the text of its QR code,
        /// "WWCP/TA-2:" and the Base45 of its CBOR; its URI; or its JSON.
        /// </summary>
        public static Boolean TryParseText(String                                               Text,
                                           [NotNullWhen(true)]  out RoamingNetworkTrustAnchor?  TrustAnchor,
                                           [NotNullWhen(false)] out String?                     Error)
        {

            TrustAnchor  = null;
            Error        = null;

            var text = Text.Trim();

            if (text.Length > MaxTextLength)
            {
                Error = $"The trust anchor is {text.Length} characters long; {MaxTextLength} are the most taken.";
                return false;
            }

            if (text.StartsWith('{'))
            {

                JObject json;

                try
                {
                    json = JObject.Parse(text);
                }
                catch (JsonException e)
                {
                    Error = "The trust anchor looks like JSON, and is none: " + e.Message;
                    return false;
                }

                if (!RoamingNetworkTrustAnchor.TryParse(json, out TrustAnchor, out var jsonError))
                {
                    Error = "That is no trust anchor: " + jsonError;
                    return false;
                }

                return true;

            }

            if (!RoamingNetworkTrustAnchor.TryParse(text, out TrustAnchor, out var textError))
            {
                Error = "That is no trust anchor: " + textError;
                return false;
            }

            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section, as the configuration file keeps it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("trustAnchor", TrustAnchor?.ToJSON()));

        #endregion

    }

}
