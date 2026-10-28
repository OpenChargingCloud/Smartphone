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

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// A charging ticket as the e-mobility provider issues it (its EMSP's
    /// README, "Account keys and charging tickets"): a COSE_Sign whose payload
    /// says that whoever holds the ticket's key may charge, between two
    /// moments, on the provider's word - without saying who that is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written and checked with the CBOR and COSE of Styx alone. The payload is
    /// deterministic CBOR (RFC 8949, section 4.2.1); the request is signed
    /// with ES256 twice - by the ticket's key, kid "ticket", which shows the
    /// app holds it, and by an account key of the driver, kid the SHA-256 of
    /// its certificate, which shows whose it is. The provider takes the
    /// account's signature off and signs as its ticket issuer, so that a charge
    /// point operator sees the ticket's key and the provider's word, and not
    /// the driver.
    /// </para>
    /// <para>
    /// One ticket per start: a ticket used twice links two sessions at the
    /// charge point operator. Without limits, and good for an hour: a ticket
    /// unlike every other one is a fingerprint of its own.
    /// </para>
    /// </remarks>
    public static class ChargingTickets
    {

        #region Data

        /// <summary>What the payload says it is.</summary>
        public const String   Type          = "ChargingTicket";

        /// <summary>The version of the payload.</summary>
        public const Int32    Version       = 1;

        /// <summary>The content type of the payload, in the body's protected header.</summary>
        public const String   ContentType   = "application/charging-ticket+cbor";

        /// <summary>The kid of the ticket key's own signature.</summary>
        public static readonly Byte[]  TicketKeyId  = "ticket"u8.ToArray();

        /// <summary>How long a ticket of this phone is good for: the one start it is made for.</summary>
        public static readonly TimeSpan  Validity   = TimeSpan.FromHours(1);

        #endregion


        #region NewKey()

        /// <summary>
        /// A new key on P-256: of a ticket, or of the account.
        /// </summary>
        public static ECDsa NewKey()

            => ECDsa.Create(ECCurve.NamedCurves.nistP256);

        #endregion

        #region PrivateKeyOf(Key) / PublicKeyOf(Key)

        /// <summary>
        /// A key on P-256 as Styx signs with it.
        /// </summary>
        public static ECPrivateKeyParameters PrivateKeyOf(ECDsa Key)

            => new ("EC", new BigInteger(1, Key.ExportParameters(true).D!), SecObjectIdentifiers.SecP256r1);

        /// <summary>
        /// The public half of a key on P-256 as Styx checks with it.
        /// </summary>
        public static ECPublicKeyParameters PublicKeyOf(ECDsa Key)
        {

            var parameters  = Key.ExportParameters(false);
            var curve       = SecNamedCurves.GetByOid(SecObjectIdentifiers.SecP256r1);
            var point       = curve.Curve.CreatePoint(new BigInteger(1, parameters.Q.X!), new BigInteger(1, parameters.Q.Y!));

            return new ECPublicKeyParameters("EC", point, SecObjectIdentifiers.SecP256r1);

        }

        #endregion


        #region Payload(Id, EMSP, TicketKey, NotBefore, NotAfter)

        /// <summary>
        /// The payload of a ticket, as deterministic CBOR:
        /// { "typ", "v", "id", "emsp", "key", "nbf", "exp" } - and no limits.
        /// </summary>
        /// <param name="Id">16 random bytes.</param>
        /// <param name="EMSP">The provider it is good with, as it names itself: "DE*GDF".</param>
        /// <param name="TicketKey">The ticket's key.</param>
        /// <param name="NotBefore">From when it is good.</param>
        /// <param name="NotAfter">Until when it is good.</param>
        public static Byte[] Payload(Byte[]          Id,
                                     String          EMSP,
                                     ECDsa           TicketKey,
                                     DateTimeOffset  NotBefore,
                                     DateTimeOffset  NotAfter)
        {

            var key = TicketKey.ExportParameters(false);

            return CBORValue.FromMap([
                       new (CBORValue.FromText("typ"),   CBORValue.FromText(Type)),
                       new (CBORValue.FromText("v"),     CBORValue.FromInt64(Version)),
                       new (CBORValue.FromText("id"),    CBORValue.FromBytes(Id)),
                       new (CBORValue.FromText("emsp"),  CBORValue.FromText(EMSP)),
                       new (CBORValue.FromText("key"),   CBORValue.FromMap([
                                                             new (CBORValue.FromInt64( 1), CBORValue.FromInt64(2)),    // kty: EC2
                                                             new (CBORValue.FromInt64(-1), CBORValue.FromInt64(1)),    // crv: P-256
                                                             new (CBORValue.FromInt64(-2), CBORValue.FromBytes(key.Q.X!)),
                                                             new (CBORValue.FromInt64(-3), CBORValue.FromBytes(key.Q.Y!))
                                                         ])),
                       new (CBORValue.FromText("nbf"),   CBORValue.FromInt64(NotBefore.ToUnixTimeSeconds())),
                       new (CBORValue.FromText("exp"),   CBORValue.FromInt64(NotAfter. ToUnixTimeSeconds()))
                   ]).ToByteArray(CBORWriterOptions.Canonical);

        }

        #endregion

        #region Request(Payload, TicketKey, AccountKey, AccountKeyId)

        /// <summary>
        /// The request for a ticket: the payload, signed by the ticket's key and
        /// by an account key, each naming its key in its protected header.
        /// </summary>
        /// <param name="Payload">The payload, as Payload(…) wrote it.</param>
        /// <param name="TicketKey">The ticket's key.</param>
        /// <param name="AccountKey">An account key of the driver.</param>
        /// <param name="AccountKeyId">The SHA-256 of its account certificate.</param>
        public static Byte[] Request(Byte[]  Payload,
                                     ECDsa   TicketKey,
                                     ECDsa   AccountKey,
                                     Byte[]  AccountKeyId)

            => COSESign.Sign(Payload,
                             PrivateKeyOf(TicketKey),
                             COSEHeaders.Create(COSEAlgorithm.ES256, TicketKeyId),
                             BodyProtectedHeader:  new COSEHeaders((CBORValue.FromInt64(3), CBORValue.FromText(ContentType))),
                             CanonicalizePayload:  false).
                        AddSignature(PrivateKeyOf(AccountKey),
                                     COSEHeaders.Create(COSEAlgorithm.ES256, AccountKeyId)).
                        ToByteArray();

        #endregion

        #region TryCheckIssued(Issued, Payload, TicketKey, Issuer, AccountKeyId, out Error)

        /// <summary>
        /// Whether a ticket the provider sent back is the one asked for: the
        /// payload as it was written, signed by the ticket's key and by the
        /// provider's ticket issuer - and the account's signature gone, as
        /// nothing the charge point operator sees may name the account.
        /// </summary>
        /// <param name="Issued">The ticket as the provider sent it, a COSE_Sign.</param>
        /// <param name="Payload">The payload as it was asked for.</param>
        /// <param name="TicketKey">The ticket's key.</param>
        /// <param name="Issuer">The certificate of the provider's ticket issuer.</param>
        /// <param name="AccountKeyId">The kid of the account's signature, which must not be there.</param>
        /// <param name="Error">Why it is not.</param>
        public static Boolean TryCheckIssued(Byte[]                            Issued,
                                             Byte[]                            Payload,
                                             ECDsa                             TicketKey,
                                             X509Certificate2                  Issuer,
                                             Byte[]                            AccountKeyId,
                                             [NotNullWhen(false)] out String?  Error)
        {

            if (!COSESign.TryParse(Issued, out var ticket, out var parseError))
            {
                Error = "What the provider sent back is no COSE_Sign: " + parseError;
                return false;
            }

            if (ticket.Payload is null || !ticket.Payload.AsSpan().SequenceEqual(Payload))
            {
                Error = "The provider sent back another ticket than the one asked for: its payload was changed.";
                return false;
            }

            if (ticket.Signatures.Any(signature => signature.KeyIdentifier?.AsSpan().SequenceEqual(AccountKeyId) == true))
            {
                Error = "The provider sent back the ticket with the account's signature still in it: it would tell the charge point operator whose it is.";
                return false;
            }

            var own = ticket.Signatures.FirstOrDefault(signature => signature.KeyIdentifier?.AsSpan().SequenceEqual(TicketKeyId) == true);

            if (own is null || !ticket.Verify(own, PublicKeyOf(TicketKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256))
            {
                Error = "The ticket the provider sent back is not signed by its own key.";
                return false;
            }

            using var issuerKey = Issuer.GetECDsaPublicKey();

            if (issuerKey is null ||
               !ticket.Signatures.Where(signature => !ReferenceEquals(signature, own)).
                                  Any(signature => ticket.Verify(signature, PublicKeyOf(issuerKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256)))
            {
                Error = "The ticket the provider sent back is not signed by its ticket issuer.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

        #region Thumbprint(Key)

        /// <summary>
        /// The SHA-256 of a public key's SubjectPublicKeyInfo, as hex: how a
        /// ticket key is named where its whole is too long.
        /// </summary>
        public static String Thumbprint(ECDsa Key)

            => Convert.ToHexStringLower(SHA256.HashData(Key.ExportSubjectPublicKeyInfo()));

        #endregion

    }

}
