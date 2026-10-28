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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// What the e-mobility provider says in its signature of a start or a stop
    /// request, in that signature's protected header and so covered by it: the
    /// OCPI token of this start alone, the authorization reference it sends the
    /// charge point operator with it, and when it signed.
    /// </summary>
    public sealed record ProviderCountersignature(String          Token,
                                                  String          AuthorizationReference,
                                                  DateTimeOffset  SignedAt);


    /// <summary>
    /// Start and stop requests with a charging ticket (docs/RemoteStartWithChargingTickets.md):
    /// a COSE_Sign whose payload says what the holder of the ticket wants - to
    /// charge at exactly this EVSE, now; or to end the session of exactly that
    /// start - signed by the ticket's key, and countersigned by the e-mobility
    /// provider, which vouches for it and pays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The phone signs first, kid "ticket". The provider adds its signature,
    /// kid the SHA-256 of its ticket issuer's certificate, and puts what only
    /// it sets - the token, the authorization reference, when it signed - into
    /// the protected header of its own signature: covered by its signature
    /// alone, so that the phone's stays valid. Nothing of the phone's is
    /// written anew: the body's protected header, the payload and the phone's
    /// signature come back as the bytes they went, or the request is refused.
    /// </para>
    /// <para>
    /// A request names its ticket by the SHA-256 of the ticket as the provider
    /// issued it, and a stop names its start by the SHA-256 of the start
    /// request as the provider countersigned it: the bytes the charge point
    /// operator holds of both. A nonce of 16 random bytes makes each request
    /// one that is taken once.
    /// </para>
    /// </remarks>
    public static class StartRequests
    {

        #region Data

        /// <summary>What the payload of a start request says it is.</summary>
        public const String  StartType         = "StartRequest";

        /// <summary>What the payload of a stop request says it is.</summary>
        public const String  StopType          = "StopRequest";

        /// <summary>The version of both payloads.</summary>
        public const Int32   Version           = 1;

        /// <summary>The content type of a start request, in the body's protected header.</summary>
        public const String  StartContentType  = "application/start-request+cbor";

        /// <summary>The content type of a stop request, in the body's protected header.</summary>
        public const String  StopContentType   = "application/stop-request+cbor";

        /// <summary>The label of the token in the provider's protected header.</summary>
        public const String  TokenLabel        = "token";

        /// <summary>The label of the authorization reference in the provider's protected header.</summary>
        public const String  AuthRefLabel      = "authRef";

        /// <summary>The label of when the provider signed, seconds since 1970, in its protected header.</summary>
        public const String  SignedAtLabel     = "iat";

        /// <summary>
        /// How far from the phone's clock the provider may say it signed.
        /// </summary>
        public static readonly TimeSpan  ClockSkew  = TimeSpan.FromMinutes(5);

        #endregion


        #region Digest(COSE)

        /// <summary>
        /// The SHA-256 of a ticket or a request, as the bytes it is: how a
        /// request names its ticket, and a stop its start.
        /// </summary>
        public static Byte[] Digest(Byte[] COSE)

            => SHA256.HashData(COSE);

        #endregion

        #region StartPayload(Ticket, EVSEId, At, Nonce, EffectiveTree = null, TariffId = null)

        /// <summary>
        /// The payload of a start request, as deterministic CBOR:
        /// { "typ", "v", "ticket", "evse", "at", "nonce" } - and, at a station of
        /// a roaming network, "proof" and "tariff": what the driver saw.
        /// </summary>
        /// <param name="Ticket">The ticket as the provider issued it.</param>
        /// <param name="EVSEId">The EVSE, as the roaming network names it.</param>
        /// <param name="At">When the driver asked.</param>
        /// <param name="Nonce">16 random bytes.</param>
        /// <param name="EffectiveTree">The digest of the effective tree of the version of the station's proof the phone checked.</param>
        /// <param name="TariffId">The tariff of that proof the price is of.</param>
        public static Byte[] StartPayload(Byte[]          Ticket,
                                          String          EVSEId,
                                          DateTimeOffset  At,
                                          Byte[]          Nonce,
                                          Byte[]?         EffectiveTree  = null,
                                          String?         TariffId       = null)
        {

            var entries = new List<KeyValuePair<CBORValue, CBORValue>> {
                              new (CBORValue.FromText("typ"),     CBORValue.FromText(StartType)),
                              new (CBORValue.FromText("v"),       CBORValue.FromInt64(Version)),
                              new (CBORValue.FromText("ticket"),  CBORValue.FromBytes(Digest(Ticket))),
                              new (CBORValue.FromText("evse"),    CBORValue.FromText(EVSEId)),
                              new (CBORValue.FromText("at"),      CBORValue.FromInt64(At.ToUnixTimeSeconds())),
                              new (CBORValue.FromText("nonce"),   CBORValue.FromBytes(Nonce))
                          };

            if (EffectiveTree is not null)
                entries.Add(new (CBORValue.FromText("proof"),   CBORValue.FromBytes(EffectiveTree)));

            if (TariffId is not null)
                entries.Add(new (CBORValue.FromText("tariff"),  CBORValue.FromText(TariffId)));

            return CBORValue.FromMap(entries).ToByteArray(CBORWriterOptions.Canonical);

        }

        #endregion

        #region StopPayload(Ticket, Start, At, Nonce)

        /// <summary>
        /// The payload of a stop request, as deterministic CBOR:
        /// { "typ", "v", "ticket", "start", "at", "nonce" }.
        /// </summary>
        /// <param name="Ticket">The ticket as the provider issued it.</param>
        /// <param name="Start">The start request of the session, as the provider countersigned it.</param>
        /// <param name="At">When the driver asked.</param>
        /// <param name="Nonce">16 random bytes.</param>
        public static Byte[] StopPayload(Byte[]          Ticket,
                                         Byte[]          Start,
                                         DateTimeOffset  At,
                                         Byte[]          Nonce)

            => CBORValue.FromMap([
                   new (CBORValue.FromText("typ"),     CBORValue.FromText(StopType)),
                   new (CBORValue.FromText("v"),       CBORValue.FromInt64(Version)),
                   new (CBORValue.FromText("ticket"),  CBORValue.FromBytes(Digest(Ticket))),
                   new (CBORValue.FromText("start"),   CBORValue.FromBytes(Digest(Start))),
                   new (CBORValue.FromText("at"),      CBORValue.FromInt64(At.ToUnixTimeSeconds())),
                   new (CBORValue.FromText("nonce"),   CBORValue.FromBytes(Nonce))
               ]).ToByteArray(CBORWriterOptions.Canonical);

        #endregion

        #region Sign(Payload, ContentType, TicketKey)

        /// <summary>
        /// A request: the payload, signed by the ticket's key, kid "ticket" in
        /// its protected header, the content type in the body's.
        /// </summary>
        public static Byte[] Sign(Byte[]  Payload,
                                  String  ContentType,
                                  ECDsa   TicketKey)

            => COSESign.Sign(Payload,
                             ChargingTickets.PrivateKeyOf(TicketKey),
                             COSEHeaders.Create(COSEAlgorithm.ES256, ChargingTickets.TicketKeyId),
                             BodyProtectedHeader:  new COSEHeaders((CBORValue.FromInt64(3), CBORValue.FromText(ContentType))),
                             CanonicalizePayload:  false).
                        ToByteArray();

        #endregion

        #region TryCheckCountersigned(Countersigned, Sent, TicketKey, Issuer, Now, Of, out Countersignature, out Error)

        /// <summary>
        /// Whether a request the provider sent back is the one the phone sent,
        /// countersigned: the body's protected header, the payload and the
        /// phone's own signature as the bytes they went, and one signature
        /// more, by the provider's ticket issuer, whose protected header says
        /// the token, the authorization reference and when it signed - near the
        /// phone's clock, and for a stop the token and the reference of its
        /// start.
        /// </summary>
        /// <param name="Countersigned">The request as the provider sent it back, a COSE_Sign.</param>
        /// <param name="Sent">The request as the phone sent it.</param>
        /// <param name="TicketKey">The ticket's key.</param>
        /// <param name="Issuer">The certificate of the provider's ticket issuer.</param>
        /// <param name="Now">The phone's clock.</param>
        /// <param name="Of">For a stop: what the provider said of its start.</param>
        /// <param name="Countersignature">What the provider says in its signature.</param>
        /// <param name="Error">Why it is not.</param>
        public static Boolean TryCheckCountersigned(Byte[]                                                Countersigned,
                                                    Byte[]                                                Sent,
                                                    ECDsa                                                 TicketKey,
                                                    X509Certificate2                                      Issuer,
                                                    DateTimeOffset                                        Now,
                                                    ProviderCountersignature?                             Of,
                                                    [NotNullWhen(true)]  out ProviderCountersignature?    Countersignature,
                                                    [NotNullWhen(false)] out String?                      Error)
        {

            Countersignature = null;

            if (!COSESign.TryParse(Countersigned, out var signed, out var parseError))
            {
                Error = "What the provider sent back is no COSE_Sign: " + parseError;
                return false;
            }

            var sent = COSESign.Parse(Sent);
            var mine = sent.Signatures[0];

            if (!signed.ProtectedHeaderBytes.AsSpan().SequenceEqual(sent.ProtectedHeaderBytes) ||
                 signed.Payload is null || !signed.Payload.AsSpan().SequenceEqual(sent.Payload))
            {
                Error = "The provider sent back another request than this phone signed: its payload or its protected header was changed.";
                return false;
            }

            var own = signed.Signatures.FirstOrDefault(signature => signature.ProtectedHeaderBytes.AsSpan().SequenceEqual(mine.ProtectedHeaderBytes) &&
                                                                    signature.Signature.           AsSpan().SequenceEqual(mine.Signature));

            if (own is null || !signed.Verify(own, ChargingTickets.PublicKeyOf(TicketKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256))
            {
                Error = "The provider sent the request back without this phone's own signature in it as it was made.";
                return false;
            }

            if (signed.Signatures.Count != 2)
            {
                Error = $"The provider sent the request back with {signed.Signatures.Count} signatures: a countersigned request has the ticket key's and the provider's.";
                return false;
            }

            var theirs = signed.Signatures.First(signature => !ReferenceEquals(signature, own));

            if (theirs.ProtectedHeader.KeyIdentifier is not Byte[] kid || !kid.AsSpan().SequenceEqual(SHA256.HashData(Issuer.RawData)))
            {
                Error = "The provider countersigned the request with another key than its ticket issuer's: its protected header names none, or another.";
                return false;
            }

            using var issuerKey = Issuer.GetECDsaPublicKey();

            if (issuerKey is null)
            {
                Error = "The certificate of the provider's ticket issuer is of no key on P-256.";
                return false;
            }

            if (!signed.Verify(theirs, ChargingTickets.PublicKeyOf(issuerKey), out var verifyError, ExpectedAlgorithm: COSEAlgorithm.ES256))
            {
                Error = "The request the provider sent back is not countersigned by its ticket issuer: " + verifyError;
                return false;
            }

            // Only what the protected header says is covered by the signature:
            // an unprotected token is anybody's to change.
            String? Text(String Label)

                => theirs.ProtectedHeader.TryGet(CBORValue.FromText(Label), out var value) && value.TryGetText(out var text) && text.Length > 0
                       ? text
                       : null;

            if (Text(TokenLabel) is not String token || Text(AuthRefLabel) is not String authRef)
            {
                Error = "The provider's signature says no token or no authorization reference in its protected header, where its signature would cover them.";
                return false;
            }

            if (!theirs.ProtectedHeader.TryGet(CBORValue.FromText(SignedAtLabel), out var iat) || !iat.TryGetInt64(out var seconds) ||
                 seconds < 0 || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                Error = "The provider's signature says not when it was made, in its protected header.";
                return false;
            }

            var signedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);

            if ((signedAt - Now).Duration() > ClockSkew)
            {
                Error = $"The provider says it countersigned at {signedAt:o}, which is more than {ClockSkew.TotalMinutes:0} minutes off this phone's clock ({Now:o}).";
                return false;
            }

            if (Of is not null && (token != Of.Token || authRef != Of.AuthorizationReference))
            {
                Error = "The provider countersigned the stop with another token or authorization reference than its start.";
                return false;
            }

            Countersignature  = new ProviderCountersignature(token, authRef, signedAt);
            Error             = null;
            return true;

        }

        #endregion

    }

}
