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

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// The key statement of the operator of a roaming network, as the network
    /// served it, evaluated against its trust anchor and the identity keys
    /// trusted here: what an administrator looks at before trusting a key.
    /// </summary>
    /// <remarks>
    /// Its identity keys and who holds them are what the statement says, and
    /// whether it is signed validly is what can be checked of it - by keys it
    /// names itself, so that a valid signature says nothing yet of who made it.
    /// Whether a key is the operator's is what the administrator decides, by
    /// what they know of it besides, as of a QR code they trust.
    /// </remarks>
    /// <param name="CheckedAt">When it was asked and evaluated.</param>
    /// <param name="From">The URL that served it.</param>
    /// <param name="KeyStatement">The key statement, where one could be read.</param>
    /// <param name="Evaluation">How it fared, where it is one of the network of the trust anchor.</param>
    /// <param name="Error">Why there is none, or why it was not evaluated.</param>
    public sealed record KeyStatementCheck(DateTimeOffset                  CheckedAt,
                                           String?                         From,
                                           RoamingNetworkKeyStatement?     KeyStatement,
                                           RoamingNetworkTrustEvaluation?  Evaluation,
                                           String?                         Error)
    {

        public JObject ToJSON()

            => new (new JProperty("checkedAt",     CheckedAt.ToString("o")),
                    new JProperty("from",          From),
                    new JProperty("error",         Error),
                    new JProperty("keyStatement",  KeyStatement is null ? null : new JObject(
                        new JProperty("roamingNetworkId",  KeyStatement.RoamingNetworkId.ToString()),
                        new JProperty("operatorId",        KeyStatement.RoamingNetworkOperatorId.ToString()),
                        new JProperty("signedAt",          KeyStatement.SignedAt.  ToString("o")),
                        new JProperty("validUntil",        KeyStatement.ValidUntil.ToString("o")),
                        new JProperty("minConsensus",      KeyStatement.MinConsensus),
                        new JProperty("signedBy",          new JArray(KeyStatement.Signatures.Select(signature => signature.KeyId).ToArray())),
                        new JProperty("identityKeys",      new JArray(KeyStatement.IdentityKeys.Select(key => new JObject(
                            new JProperty("id",         key.Id),
                            new JProperty("holder",     key.Holder),
                            new JProperty("notBefore",  key.NotBefore?.ToString("o")),
                            new JProperty("notAfter",   key.NotAfter?. ToString("o")),
                            new JProperty("key",        key.ToJSON(Embedded: true)["key"]?.Value<String>())
                        ))))
                    )),
                    new JProperty("evaluation",    Evaluation is null ? null : new JObject(
                        new JProperty("signaturesValid",    Evaluation.SignaturesValid),
                        new JProperty("signersTrusted",     Evaluation.SignersTrusted),
                        new JProperty("quorumReached",      Evaluation.QuorumReached),
                        new JProperty("withinValidity",     Evaluation.WithinValidity),
                        new JProperty("canTrustKeys",       Evaluation.CanTrustKeys),
                        new JProperty("trustedHolders",     Evaluation.TrustedHolderCount),
                        new JProperty("requiredConsensus",  Evaluation.RequiredConsensus),
                        new JProperty("signatures",         Evaluation.SignatureCount),
                        new JProperty("error",              Evaluation.VerificationError)
                    )));

    }

}
