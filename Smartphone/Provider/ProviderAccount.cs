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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json.Linq;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// The account of this phone at an e-mobility provider: where the provider
    /// is, who the phone is there and with which password, the provider's name
    /// for itself and its ticket issuer, and the phone's account key with its
    /// certificate.
    /// </summary>
    /// <remarks>
    /// Kept in a file nobody but the account the phone runs under may read: it
    /// holds a password and a private key.
    /// </remarks>
    /// <param name="URL">Where the provider is: its web interface, below which "/ext" and "/api" are.</param>
    /// <param name="Username">Who the phone is there.</param>
    /// <param name="EMail">The e-mail address of the account.</param>
    /// <param name="Password">Its password: one the phone made, or the driver's.</param>
    /// <param name="Party">The provider's name for itself in a ticket: "DE*GDF".</param>
    /// <param name="IssuerPEM">The certificate of the provider's ticket issuer, as the phone was given it when it signed in.</param>
    /// <param name="AccountKeyPEM">The phone's account key, PKCS#8.</param>
    /// <param name="AccountCertificatePEM">Its certificate, by the provider's account CA.</param>
    /// <param name="AccountCertificateId">The SHA-256 of that certificate, hex: the kid of the account's signature.</param>
    /// <param name="Since">When the phone signed up or in.</param>
    /// <param name="MadeUp">Whether the phone made the account, and its password, itself.</param>
    public sealed record ProviderAccount(String          URL,
                                         String          Username,
                                         String          EMail,
                                         String          Password,
                                         String          Party,
                                         String          IssuerPEM,
                                         String          AccountKeyPEM,
                                         String          AccountCertificatePEM,
                                         String          AccountCertificateId,
                                         DateTimeOffset  Since,
                                         Boolean         MadeUp)
    {

        #region AccountKey() / Issuer() / AccountCertificate()

        /// <summary>
        /// The phone's account key.
        /// </summary>
        public ECDsa AccountKey()
        {
            var key = ECDsa.Create();
            key.ImportFromPem(AccountKeyPEM);
            return key;
        }

        /// <summary>
        /// The certificate of the provider's ticket issuer.
        /// </summary>
        public X509Certificate2 Issuer()

            => X509Certificate2.CreateFromPem(IssuerPEM);

        /// <summary>
        /// The certificate of the phone's account key.
        /// </summary>
        public X509Certificate2 AccountCertificate()

            => X509Certificate2.CreateFromPem(AccountCertificatePEM);

        #endregion


        #region ToStorageJSON() / TryParseStorage(JSON, out Account, out Error)

        /// <summary>
        /// The account as its file keeps it, password and key and all.
        /// </summary>
        public JObject ToStorageJSON()

            => new (new JProperty("url",                    URL),
                    new JProperty("username",               Username),
                    new JProperty("email",                  EMail),
                    new JProperty("password",               Password),
                    new JProperty("madeUp",                 MadeUp),
                    new JProperty("party",                  Party),
                    new JProperty("issuer",                 IssuerPEM),
                    new JProperty("accountKey",             AccountKeyPEM),
                    new JProperty("accountCertificate",     AccountCertificatePEM),
                    new JProperty("accountCertificateId",   AccountCertificateId),
                    new JProperty("since",                  Since.ToString("o")));

        /// <summary>
        /// The account as its file kept it.
        /// </summary>
        public static Boolean TryParseStorage(JObject                                    JSON,
                                              [NotNullWhen(true)]  out ProviderAccount?  Account,
                                              [NotNullWhen(false)] out String?           Error)
        {

            Account = null;

            try
            {

                String Text(String Name) => JSON.Value<String>(Name) is { Length: > 0 } text ? text : throw new FormatException($"'{Name}' is missing.");

                Account = new ProviderAccount(Text("url"),
                                              Text("username"),
                                              JSON.Value<String>("email") ?? "",
                                              Text("password"),
                                              Text("party"),
                                              Text("issuer"),
                                              Text("accountKey"),
                                              Text("accountCertificate"),
                                              Text("accountCertificateId"),
                                              DateTimeOffset.Parse(Text("since"), CultureInfo.InvariantCulture),
                                              JSON.Value<Boolean?>("madeUp") ?? false);

                // What is kept has to be what it says: a key the certificate is
                // of, and certificates that can be read.
                using var key          = Account.AccountKey();
                using var certificate  = Account.AccountCertificate();
                using var issuer       = Account.Issuer();
                using var certified    = certificate.GetECDsaPublicKey() ?? throw new FormatException("the account certificate is of no key on P-256.");

                if (!certified.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
                    throw new FormatException("the account certificate is not of the account key kept with it.");

                Error = null;
                return true;

            }
            catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
            {
                Account  = null;
                Error    = $"The account at the e-mobility provider could not be read: {e.Message}";
                return false;
            }

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The account as a page shows it: not its password, and not its key.
        /// </summary>
        public JObject ToJSON()
        {

            using var certificate  = AccountCertificate();
            using var issuer       = Issuer();

            return new JObject(
                       new JProperty("url",          URL),
                       new JProperty("username",     Username),
                       new JProperty("email",        EMail),
                       new JProperty("madeUp",       MadeUp),
                       new JProperty("party",        Party),
                       new JProperty("since",        Since.ToString("o")),
                       new JProperty("accountKey",   new JObject(
                           new JProperty("id",         AccountCertificateId),
                           new JProperty("subject",    certificate.Subject),
                           new JProperty("issuer",     certificate.Issuer),
                           new JProperty("notBefore",  new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero).ToString("o")),
                           new JProperty("notAfter",   new DateTimeOffset(certificate.NotAfter. ToUniversalTime(), TimeSpan.Zero).ToString("o"))
                       )),
                       new JProperty("ticketIssuer", new JObject(
                           new JProperty("subject",      issuer.Subject),
                           new JProperty("fingerprint",  Convert.ToHexStringLower(SHA256.HashData(issuer.RawData))),
                           new JProperty("notAfter",     new DateTimeOffset(issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero).ToString("o"))
                       ))
                   );

        }

        #endregion

    }


    /// <summary>
    /// A ticket the provider issued to this phone for one start: the ticket as
    /// it was signed, and its key - kept, as a stop and the meter values of the
    /// session are proven with it - and the start and stop requests made with
    /// it, as the provider countersigned them.
    /// </summary>
    /// <param name="Id">Its id, the 16 random bytes as hex.</param>
    /// <param name="Party">The provider it is good with.</param>
    /// <param name="NotBefore">From when it is good.</param>
    /// <param name="NotAfter">Until when it is good.</param>
    /// <param name="Ticket">The ticket, the COSE_Sign signed by its key and by the provider.</param>
    /// <param name="TicketKeyPEM">Its key, PKCS#8.</param>
    /// <param name="KeyThumbprint">The SHA-256 of the SubjectPublicKeyInfo of its key, hex.</param>
    /// <param name="IssuerFingerprint">The SHA-256 of the certificate of the provider's ticket issuer, hex.</param>
    /// <param name="EVSEId">The EVSE it was asked for.</param>
    /// <param name="IssuedAt">When the provider issued it.</param>
    /// <param name="Start">The start request made with it, as the provider countersigned it.</param>
    /// <param name="Stop">The stop request made with it, as the provider countersigned it.</param>
    public sealed record IssuedTicket(String          Id,
                                      String          Party,
                                      DateTimeOffset  NotBefore,
                                      DateTimeOffset  NotAfter,
                                      Byte[]          Ticket,
                                      String          TicketKeyPEM,
                                      String          KeyThumbprint,
                                      String          IssuerFingerprint,
                                      String          EVSEId,
                                      DateTimeOffset  IssuedAt,
                                      Byte[]?         Start  = null,
                                      Byte[]?         Stop   = null)
    {

        /// <summary>
        /// What a charging session keeps of it - nothing secret.
        /// </summary>
        public ChargingTicketReference Reference

            => new (Id, Party, NotBefore, NotAfter, KeyThumbprint, IssuerFingerprint);

        /// <summary>
        /// Its key.
        /// </summary>
        public ECDsa TicketKey()
        {
            var key = ECDsa.Create();
            key.ImportFromPem(TicketKeyPEM);
            return key;
        }

        public JObject ToStorageJSON()
        {

            var json = new JObject(
                           new JProperty("id",                 Id),
                           new JProperty("party",              Party),
                           new JProperty("notBefore",          NotBefore.ToString("o")),
                           new JProperty("notAfter",           NotAfter. ToString("o")),
                           new JProperty("ticket",             Convert.ToBase64String(Ticket)),
                           new JProperty("ticketKey",          TicketKeyPEM),
                           new JProperty("keyThumbprint",      KeyThumbprint),
                           new JProperty("issuerFingerprint",  IssuerFingerprint),
                           new JProperty("evseId",             EVSEId),
                           new JProperty("issuedAt",           IssuedAt.ToString("o"))
                       );

            if (Start is not null)
                json.Add(new JProperty("start", Convert.ToBase64String(Start)));

            if (Stop is not null)
                json.Add(new JProperty("stop",  Convert.ToBase64String(Stop)));

            return json;

        }

        /// <summary>
        /// A ticket as its file kept it, its key with it.
        /// </summary>
        public static Boolean TryParseStorage(JObject                                 JSON,
                                              [NotNullWhen(true)]  out IssuedTicket?  Ticket,
                                              [NotNullWhen(false)] out String?        Error)
        {

            Ticket = null;

            try
            {

                String         Text  (String Name) => JSON.Value<String>(Name) is { Length: > 0 } text ? text : throw new FormatException($"'{Name}' is missing.");
                DateTimeOffset Time  (String Name) => DateTimeOffset.Parse(Text(Name), CultureInfo.InvariantCulture);
                Byte[]?        Bytes (String Name) => JSON.Value<String>(Name) is { Length: > 0 } text ? Convert.FromBase64String(text) : null;

                Ticket = new IssuedTicket(Text ("id"),
                                          Text ("party"),
                                          Time ("notBefore"),
                                          Time ("notAfter"),
                                          Bytes("ticket") ?? throw new FormatException("'ticket' is missing."),
                                          Text ("ticketKey"),
                                          Text ("keyThumbprint"),
                                          Text ("issuerFingerprint"),
                                          Text ("evseId"),
                                          Time ("issuedAt"),
                                          Bytes("start"),
                                          Bytes("stop"));

                // A key that is the ticket's, or none: it is what proves it.
                using var key = Ticket.TicketKey();

                if (ChargingTickets.Thumbprint(key) != Ticket.KeyThumbprint)
                    throw new FormatException("its key is not the one of the ticket.");

                Error = null;
                return true;

            }
            catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
            {
                Ticket  = null;
                Error   = $"The charging ticket '{JSON.Value<String>("id") ?? "without an id"}' could not be read: {e.Message}";
                return false;
            }

        }

    }


    /// <summary>
    /// What a charging session keeps of the ticket it was started with: which
    /// ticket, good when, and whose keys - nothing secret, as the ticket key
    /// itself stays with the ticket.
    /// </summary>
    public sealed record ChargingTicketReference(String          Id,
                                                 String          Party,
                                                 DateTimeOffset  NotBefore,
                                                 DateTimeOffset  NotAfter,
                                                 String          KeyThumbprint,
                                                 String          IssuerFingerprint)
    {

        public JObject ToJSON()

            => new (new JProperty("id",                 Id),
                    new JProperty("party",              Party),
                    new JProperty("notBefore",          NotBefore.ToString("o")),
                    new JProperty("notAfter",           NotAfter. ToString("o")),
                    new JProperty("keyThumbprint",      KeyThumbprint),
                    new JProperty("issuerFingerprint",  IssuerFingerprint));

        public static ChargingTicketReference? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json ||
                json.Value<String>("id")                is not { Length: > 0 } id     ||
                json.Value<String>("party")             is not String          party  ||
                json.Value<String>("keyThumbprint")     is not String          key    ||
                json.Value<String>("issuerFingerprint") is not String          issuer ||
               !DateTimeOffset.TryParse(json.Value<String>("notBefore"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var notBefore) ||
               !DateTimeOffset.TryParse(json.Value<String>("notAfter"),  CultureInfo.InvariantCulture, DateTimeStyles.None, out var notAfter))
                return null;

            return new ChargingTicketReference(id, party, notBefore, notAfter, key, issuer);

        }

    }


    /// <summary>
    /// What a charging session keeps of its start through the e-mobility
    /// provider: the OCPI token of this start alone and the authorization
    /// reference the provider sent the charge point operator, the SHA-256 of
    /// the start request as the provider countersigned it and when it did -
    /// and the same of the stop, once there was one. The requests themselves
    /// are kept with the ticket.
    /// </summary>
    public sealed record ChargingStartReference(String           Token,
                                                String           AuthorizationReference,
                                                String           RequestDigest,
                                                DateTimeOffset   CountersignedAt,
                                                String?          StopRequestDigest    = null,
                                                DateTimeOffset?  StopCountersignedAt  = null)
    {

        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("token",                   Token),
                           new JProperty("authorizationReference",  AuthorizationReference),
                           new JProperty("requestDigest",           RequestDigest),
                           new JProperty("countersignedAt",         CountersignedAt.ToString("o"))
                       );

            if (StopRequestDigest is not null && StopCountersignedAt is DateTimeOffset stopped)
            {
                json.Add(new JProperty("stopRequestDigest",    StopRequestDigest));
                json.Add(new JProperty("stopCountersignedAt",  stopped.ToString("o")));
            }

            return json;

        }

        public static ChargingStartReference? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json ||
                json.Value<String>("token")                   is not { Length: > 0 } token    ||
                json.Value<String>("authorizationReference")  is not { Length: > 0 } authRef  ||
                json.Value<String>("requestDigest")           is not { Length: > 0 } digest   ||
               !DateTimeOffset.TryParse(json.Value<String>("countersignedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var countersignedAt))
                return null;

            var stopDigest = json.Value<String>("stopRequestDigest");

            return new ChargingStartReference(token,
                                              authRef,
                                              digest,
                                              countersignedAt,
                                              stopDigest,
                                              stopDigest is not null && DateTimeOffset.TryParse(json.Value<String>("stopCountersignedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var stopped)
                                                  ? stopped
                                                  : null);

        }

    }

}
