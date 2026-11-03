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
using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// One identity key of the trust anchor that an administrator trusts here:
    /// by its thumbprint, since when and by whom.
    /// </summary>
    /// <param name="Id">The thumbprint of the key, as the trust anchor names it.</param>
    /// <param name="AcceptedAt">When it was trusted.</param>
    /// <param name="AcceptedBy">Who trusted it.</param>
    public sealed record TrustedIdentityKey(String          Id,
                                            DateTimeOffset  AcceptedAt,
                                            String          AcceptedBy)
    {

        public JObject ToJSON()

            => new (new JProperty("id",          Id),
                    new JProperty("acceptedAt",  AcceptedAt.ToString("o")),
                    new JProperty("acceptedBy",  AcceptedBy));

        public static Boolean TryParse(JToken                                        JSON,
                                       [NotNullWhen(true)]  out TrustedIdentityKey?  Key,
                                       [NotNullWhen(false)] out String?              Error)
        {

            Key    = null;
            Error  = "A trusted identity key is { \"id\", \"acceptedAt\", \"acceptedBy\" }.";

            if (JSON is not JObject json                      ||
                json["id"]?.Type         != JTokenType.String ||
                json["acceptedBy"]?.Type != JTokenType.String)
            {
                return false;
            }

            // The configuration file is read with its dates as dates, a page's
            // JSON with them as text.
            DateTimeOffset acceptedAt;

            switch (json["acceptedAt"])
            {

                case JValue { Type: JTokenType.Date, Value: DateTimeOffset offset }:
                    acceptedAt = offset;
                    break;

                case JValue { Type: JTokenType.Date, Value: DateTime dateTime }:
                    acceptedAt = new DateTimeOffset(dateTime);
                    break;

                case JValue { Type: JTokenType.String } text when DateTimeOffset.TryParse(text.Value<String>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed):
                    acceptedAt = parsed;
                    break;

                default:
                    return false;

            }

            Key    = new TrustedIdentityKey(json["id"]!.Value<String>()!, acceptedAt, json["acceptedBy"]!.Value<String>()!);
            Error  = null;
            return true;

        }

    }


    /// <summary>
    /// The roaming network the map takes its charging stations from: the
    /// "roamingNetwork" section of the configuration file, which holds its
    /// trust anchor and those of its identity keys an administrator trusts -
    /// or no anchor, and the map shows its demo stations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trust anchor is what an app pins of a roaming network (WWCP_POI's
    /// SPECIFICATION.md, section 8.6): where the network is served, one URL or
    /// more, and the thumbprints of the identity keys of its operator, with how
    /// many of their holders must agree. It is what the operator prints as a QR
    /// code, on its website or in its documents. A QR code is as trustworthy as
    /// the place it was found: one stuck on a charging station may be anybody's.
    /// </para>
    /// <para>
    /// So setting a trust anchor trusts nothing yet, as in every app of the
    /// family (WWCP_POI's trust contexts, ChargyWebApp): an administrator
    /// trusts its identity keys one by one, once their key statement was looked
    /// at, and a proof is checked against those alone - against a trust context
    /// evaluated of them, at the time of the check, for every check. Neither are
    /// the servers trusted, which only pass on what is signed, nor a key a proof
    /// names of itself.
    /// </para>
    /// <para>
    /// The anchor is taken as the text of its QR code, <c>WWCP/TA-2:</c> and
    /// the Base45 of its CBOR, as its URI, or as its JSON, and kept as its JSON,
    /// which a person can read:
    /// <code>
    /// "roamingNetwork": {
    ///     "trustAnchor": { "@id": "QA", "urls": [ "https://hub.example/poi/RNs/QA" ], "keys": [ "a1…" ] },
    ///     "trustedKeys": [ { "id": "a1…", "acceptedAt": "2026-11-03T09:00:00.0000000+00:00", "acceptedBy": "admin" } ]
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="TrustAnchor">The trust anchor of the roaming network, or null for none.</param>
    /// <param name="TrustedKeys">Those of its identity keys trusted here.</param>
    public sealed record RoamingNetworkConfiguration(RoamingNetworkTrustAnchor?         TrustAnchor,
                                                     IReadOnlyList<TrustedIdentityKey>  TrustedKeys)
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
        public static readonly RoamingNetworkConfiguration Default = new (null, []);

        #endregion

        #region Properties

        /// <summary>
        /// Whether a roaming network is set.
        /// </summary>
        [MemberNotNullWhen(true, nameof(TrustAnchor))]
        public Boolean IsSet
            => TrustAnchor is not null;

        /// <summary>
        /// The thumbprints of the identity keys trusted here.
        /// </summary>
        public IEnumerable<String> TrustedKeyIds
            => TrustedKeys.Select(key => key.Id);

        /// <summary>
        /// How many holders of the trust anchor have an identity key trusted
        /// here: two keys of one holder count once.
        /// </summary>
        public Int32 TrustedHolders
            => TrustAnchor?.Holders.Count(keys => keys.Any(key => TrustedKeys.Any(trusted => trusted.Id == key))) ?? 0;

        /// <summary>
        /// Whether keys of as many holders are trusted here as the trust anchor
        /// requires to agree - the least a proof needs to hold; its key
        /// statement may require more.
        /// </summary>
        public Boolean QuorumTrusted
            => IsSet && TrustedHolders >= TrustAnchor.MinConsensus;

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
        /// A "roamingNetwork" section as the configuration file keeps it: its
        /// "trustAnchor" as text - of a QR code, a URI or JSON - or as JSON,
        /// empty or absent for none; and its "trustedKeys", each one the anchor
        /// names.
        /// </summary>
        public static Boolean TryParse(JObject                                                JSON,
                                       [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                       Error)
        {

            Configuration = null;

            if (!TryParseTrustAnchor(JSON["trustAnchor"], out var trustAnchor, out Error))
                return false;

            if (trustAnchor is null)
            {
                Configuration = Default;
                return true;
            }

            var trustedKeys = new List<TrustedIdentityKey>();

            if (JSON["trustedKeys"] is JToken trustedKeysJSON && trustedKeysJSON.Type != JTokenType.Null)
            {

                if (trustedKeysJSON is not JArray array)
                {
                    Error = "'trustedKeys' is a list of the identity keys trusted.";
                    return false;
                }

                foreach (var item in array)
                {

                    if (!TrustedIdentityKey.TryParse(item, out var trustedKey, out Error))
                        return false;

                    if (!trustAnchor.Keys.Contains(trustedKey.Id))
                    {
                        Error = $"The trust anchor names no identity key '{trustedKey.Id}' to trust.";
                        return false;
                    }

                    if (!trustedKeys.Any(known => known.Id == trustedKey.Id))
                        trustedKeys.Add(trustedKey);

                }

            }

            Configuration = new RoamingNetworkConfiguration(trustAnchor, trustedKeys);
            return true;

        }

        #endregion

        #region TryParseChange(JSON, Who, At, out Configuration, out Error)

        /// <summary>
        /// What a page sent to change this section: its "trustAnchor" as text or
        /// JSON, empty for none, and optionally the "trustedKeys" to trust, by
        /// their thumbprints - each one the anchor names. Without them, the keys
        /// trusted so far stay trusted where the anchor stays the same - the same
        /// network, holders, quorum and URLs - and none is where it changed.
        /// </summary>
        /// <param name="JSON">What the page sent.</param>
        /// <param name="Who">Who sent it: who trusts a key newly trusted.</param>
        /// <param name="At">When: since when a key newly trusted is.</param>
        /// <param name="Configuration">The section as it is to be.</param>
        /// <param name="Error">Why it cannot be.</param>
        public Boolean TryParseChange(JObject                                                JSON,
                                      String                                                 Who,
                                      DateTimeOffset                                         At,
                                      [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                      [NotNullWhen(false)] out String?                       Error)
        {

            Configuration = null;

            if (!TryParseTrustAnchor(JSON["trustAnchor"], out var trustAnchor, out Error))
                return false;

            if (trustAnchor is null)
            {
                Configuration = Default;
                return true;
            }

            var samePolicy  = IsSet && TrustTransition.PolicyIdOf(TrustAnchor) == TrustTransition.PolicyIdOf(trustAnchor);
            var kept        = samePolicy ? TrustedKeys : [];

            if (JSON["trustedKeys"] is not JToken trustedKeysJSON || trustedKeysJSON.Type == JTokenType.Null)
            {
                Configuration = new RoamingNetworkConfiguration(trustAnchor, kept);
                return true;
            }

            if (trustedKeysJSON is not JArray array || array.Any(item => item.Type != JTokenType.String))
            {
                Error = "'trustedKeys' is a list of the thumbprints of the identity keys to trust.";
                return false;
            }

            var trustedKeys = new List<TrustedIdentityKey>();

            foreach (var id in array.Values<String>().OfType<String>().Select(id => id.Trim()).Distinct())
            {

                if (!trustAnchor.Keys.Contains(id))
                {
                    Error = $"The trust anchor names no identity key '{id}' to trust.";
                    return false;
                }

                trustedKeys.Add(kept.FirstOrDefault(known => known.Id == id) ?? new TrustedIdentityKey(id, At, Who));

            }

            Configuration = new RoamingNetworkConfiguration(trustAnchor, trustedKeys);
            return true;

        }

        #endregion

        #region (static) TryParseTrustAnchor(JSON, out TrustAnchor, out Error)

        /// <summary>
        /// A "trustAnchor" as text - of a QR code, a URI or JSON - or as JSON;
        /// null where it is empty or absent.
        /// </summary>
        private static Boolean TryParseTrustAnchor(JToken?                                   JSON,
                                                   out RoamingNetworkTrustAnchor?            TrustAnchor,
                                                   [NotNullWhen(false)] out String?          Error)
        {

            TrustAnchor  = null;
            Error        = null;

            if (JSON is null || JSON.Type == JTokenType.Null ||
               (JSON.Type == JTokenType.String && JSON.Value<String>()!.Trim().Length == 0))
            {
                return true;
            }

            if (JSON is JObject anchorJSON)
            {

                if (!RoamingNetworkTrustAnchor.TryParse(anchorJSON, out TrustAnchor, out var anchorError))
                {
                    Error = "That is no trust anchor: " + anchorError;
                    return false;
                }

                return true;

            }

            if (JSON.Type == JTokenType.String)
            {

                if (!TryParseText(JSON.Value<String>()!, out var trustAnchor, out Error))
                    return false;

                TrustAnchor = trustAnchor;
                return true;

            }

            Error = "'trustAnchor' is the text of its QR code, its URI or its JSON - or empty for none.";
            return false;

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


        #region Trusting(KeyId, Who, At) / NotTrusting(KeyId)

        /// <summary>
        /// This section with the given identity key of the trust anchor trusted
        /// as well - since it was, where it already is.
        /// </summary>
        public RoamingNetworkConfiguration Trusting(String          KeyId,
                                                    String          Who,
                                                    DateTimeOffset  At)

            => TrustedKeys.Any(key => key.Id == KeyId)
                   ? this
                   : this with { TrustedKeys = [ .. TrustedKeys, new TrustedIdentityKey(KeyId, At, Who) ] };

        /// <summary>
        /// This section with the given identity key no longer trusted.
        /// </summary>
        public RoamingNetworkConfiguration NotTrusting(String KeyId)

            => this with { TrustedKeys = [ .. TrustedKeys.Where(key => key.Id != KeyId) ] };

        #endregion

        #region Evaluate(Proof, At, out Error)

        /// <summary>
        /// The key statement of the roaming network operator the given proof
        /// gives, evaluated against the trust anchor and the identity keys
        /// trusted here at the given time: its signatures, its signers, its
        /// quorum and its validity, and the trust context to check the proof
        /// against - where all of them hold. Null, and why, where the proof
        /// gives no key statement of this network.
        /// </summary>
        public RoamingNetworkTrustEvaluation? Evaluate(RoamingNetworkProof  Proof,
                                                       DateTimeOffset       At,
                                                       out String?          Error)
        {

            Error = null;

            if (!IsSet)
            {
                Error = "There is no trust anchor to check against.";
                return null;
            }

            if (!Proof.TryGetKeyStatement(out var keyStatement, out var identityKeys, out var keyStatementError))
            {
                Error = $"It gives no key statement of the operator of the roaming network: {keyStatementError}";
                return null;
            }

            return Evaluate(keyStatement, identityKeys, At, out Error);

        }

        /// <summary>
        /// The given key statement of the roaming network operator, evaluated
        /// against the trust anchor and the identity keys trusted here at the
        /// given time - null, and why, where it is one of another network.
        /// </summary>
        public RoamingNetworkTrustEvaluation? Evaluate(RoamingNetworkKeyStatement  KeyStatement,
                                                       IEnumerable<POIPublicKey>   OtherIdentityKeys,
                                                       DateTimeOffset              At,
                                                       out String?                 Error)
        {

            Error = null;

            if (!IsSet)
            {
                Error = "There is no trust anchor to check against.";
                return null;
            }

            if (KeyStatement.RoamingNetworkId != TrustAnchor.Id)
            {
                Error = $"Its key statement is one of the roaming network '{KeyStatement.RoamingNetworkId}', the trust anchor of '{TrustAnchor.Id}'.";
                return null;
            }

            return RoamingNetworkTrustContext.Evaluate(TrustAnchor, KeyStatement, TrustedKeyIds, OtherIdentityKeys, At);

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section, as the configuration file keeps it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("trustAnchor",  TrustAnchor?.ToJSON()),
                    new JProperty("trustedKeys",  new JArray(TrustedKeys.Select(key => key.ToJSON()))));

        #endregion

    }

}
