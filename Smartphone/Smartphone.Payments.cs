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

using System.Collections.Concurrent;
using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Charging;

using ChargingSession  = cloud.charging.open.Smartphone.Charging.ChargingSession;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class Smartphone
    {

        #region Data

        /// <summary>
        /// What the payment link of a session names as its "@context".
        /// </summary>
        public const String PaymentJSONLDContext = "https://open.charging.cloud/contexts/wwcp/webPayment/payment/v1";

        /// <summary>
        /// How long to wait before the payment link of a session is asked
        /// again, at first: doubled every time, up to <see cref="PaymentRetryMax"/>.
        /// </summary>
        public static readonly TimeSpan  PaymentRetryFirst  = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long to wait at most before the payment link is asked again.
        /// </summary>
        public static readonly TimeSpan  PaymentRetryMax    = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How long after the end of a session its receipt is waited for.
        /// </summary>
        public static readonly TimeSpan  ReceiptWait        = TimeSpan.FromDays(7);

        /// <summary>
        /// The payment links asked now, by the session.
        /// </summary>
        private readonly ConcurrentDictionary<String, (CancellationTokenSource Stop, Task Asking)>  paymentFollowers  = new();

        #endregion


        #region AskPaymentNowAsync(Id, Who, CancellationToken = default)

        /// <summary>
        /// Ask the payment link the web payment page gave for a session now:
        /// its price agreement and its receipt, where they did not come yet -
        /// each checked as it can be, and the session disputed where one does
        /// not hold.
        /// </summary>
        /// <param name="Id">The session.</param>
        /// <param name="Who">Who asked, for the log.</param>
        /// <param name="CancellationToken">To stop asking the page.</param>
        public async Task<ChargingDispute> AskPaymentNowAsync(String             Id,
                                                              String             Who,
                                                              CancellationToken  CancellationToken  = default)
        {

            var session = Sessions.Get(Id);

            if (session is null)
                return ChargingDispute.Refused(ChargingRefusal.NotFound, $"There is no charging session '{Id}' on this phone.");

            if (session.LiveLink is not LiveLinkFollow { PaymentLink: not null } follow)
                return ChargingDispute.Refused(ChargingRefusal.Conflict, $"The web payment page of its operator gave no payment link for the session at {session.EVSEId}.");

            await AskPaymentAsync(Id, CancellationToken);

            var paid = CheckPayment(Id) ?? session;

            Log.Info($"'{Who}' asked the payment link of the session {Id} at {session.LocationName}, {session.EVSEId}, at {follow.PaymentLinkHost}: " +
                     PaymentInWords(paid.LiveLink?.Payment),
                     "charging", "web");

            await DisputePendingAsync(paid, true, CancellationToken);

            StartPaying(Sessions.Get(Id) ?? paid);

            return new ChargingDispute(Sessions.Get(Id) ?? paid, null, ChargingRefusal.None);

        }

        #endregion


        #region (private) StartPaying(Session)

        /// <summary>
        /// Ask the payment link of the given session until its payment is
        /// settled, unless it is asked already or the phone does not run.
        /// </summary>
        private void StartPaying(ChargingSession Session)
        {

            lock (liveLinksLock)
            {

                if (!followsLiveLinks || Session.LiveLink is not LiveLinkFollow { PaymentLink: not null } follow ||
                    PaymentSettled(follow) || paymentFollowers.ContainsKey(Session.Id))
                    return;

                var stop = new CancellationTokenSource();

                paymentFollowers[Session.Id] = (stop, Task.Run(() => PayingAsync(Session.Id, stop.Token)));

            }

        }

        /// <summary>
        /// Whether the payment of a session is settled: its receipt came and
        /// was checked, and its price agreement as well, where one came.
        /// </summary>
        private static Boolean PaymentSettled(LiveLinkFollow Follow)

            => Follow.Payment is { ReceiptCheck: not null } payment &&
               (payment.Agreement is null || payment.AgreementHolds is not null);

        #endregion

        #region (private) PayingAsync(Id, CancellationToken)

        /// <summary>
        /// Ask the payment link of a session again and again - after a pause
        /// growing from <see cref="PaymentRetryFirst"/> to <see cref="PaymentRetryMax"/>
        /// - until its price agreement and its receipt came, each kept as it
        /// came and checked once it can be: the agreement once the signed start
        /// value held, the receipt once the session ended and its end value was
        /// decided. Where one does not hold, the session is disputed through
        /// the dispute link of the page. No longer than <see cref="ReceiptWait"/>
        /// after the session ended.
        /// </summary>
        private async Task PayingAsync(String             Id,
                                       CancellationToken  CancellationToken)
        {

            var retry = PaymentRetryFirst;

            try
            {

                while (!CancellationToken.IsCancellationRequested)
                {

                    var session = Sessions.Get(Id);

                    if (session?.LiveLink is not LiveLinkFollow { PaymentLink: not null } follow || PaymentSettled(follow))
                        return;

                    var now = TimeProvider.GetUtcNow();

                    if (follow.EndedAt is DateTimeOffset ended && now - ended > ReceiptWait && follow.Payment?.Receipt is null)
                    {
                        Follow(Id, kept => kept with { Payment = (kept.Payment ?? new LiveLinkPayment()) with { Problem = $"No receipt came within {ReceiptWait.TotalDays:0} days of the end: its payment link is asked no more." } });
                        Log.Warning($"The web payment page gave no receipt for the session {Id} at {session.LocationName}, {session.EVSEId}, within {ReceiptWait.TotalDays:0} days of its end: its payment link is asked no more.",
                                    "charging", "web");
                        return;
                    }

                    var before = follow.Payment;

                    if (follow.Payment?.Agreement is null || follow.Payment.Receipt is null)
                        await AskPaymentAsync(Id, CancellationToken);

                    var paid = CheckPayment(Id);

                    if (paid?.LiveLink is LiveLinkFollow paidFollow)
                    {

                        if (paidFollow.Payment?.DisputeBecause is not null)
                            await DisputePendingAsync(paid, true, CancellationToken);

                        if (PaymentSettled(paidFollow))
                            return;

                        if (paidFollow.Payment?.Agreement is not null && before?.Agreement is null ||
                            paidFollow.Payment?.Receipt   is not null && before?.Receipt   is null)
                            retry = PaymentRetryFirst;

                    }

                    await Task.Delay(retry, TimeProvider, CancellationToken);

                    retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, PaymentRetryMax.Ticks));

                }

            }
            catch (OperationCanceledException)
            {
                // The phone stops.
            }
            catch (Exception e)
            {
                Log.Warning($"The payment link of the session {Id} is not asked any more: {e.Message}", "charging");
            }
            finally
            {
                lock (liveLinksLock)
                    if (paymentFollowers.TryRemove(Id, out var follower))
                        follower.Stop.Dispose();
            }

        }

        #endregion

        #region (private) AskPaymentAsync(Id, CancellationToken)

        /// <summary>
        /// Ask the payment link of a session once: a GET, which the page answers
        /// with its payment - the price agreement of the operator of the
        /// station for the session, once its start value is signed, and the
        /// receipt of what it took, once it took it -, 404 or nothing before.
        /// What came first is kept, as it came: nothing replaces it.
        /// </summary>
        private async Task AskPaymentAsync(String             Id,
                                           CancellationToken  CancellationToken)
        {

            if (Sessions.Get(Id)?.LiveLink is not LiveLinkFollow { PaymentLink: String link } follow || !TryLinkURL(link, out var uri))
                return;

            var now = TimeProvider.GetUtcNow();

            var (body, problem, _) = await FetchAsync(uri, "the payment of the session", uri.Authority, CancellationToken);

            JObject? agreement  = null;
            JObject? receipt    = null;

            if (body is not null && body.Trim().Length > 0)
            {
                try
                {

                    // Read as it was written: a time stays the text it was
                    // signed as.
                    using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };

                    var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });

                    if (json.Value<String>("@context") != PaymentJSONLDContext)
                        problem = $"{uri.Authority} answered no payment of a session: that names the @context '{PaymentJSONLDContext}'.";

                    else
                    {
                        agreement  = json["priceAgreement"] as JObject;
                        receipt    = json["receipt"]        as JObject;
                    }

                }
                catch (JsonException e)
                {
                    problem = $"{uri.Authority} answered no JSON document: {e.Message}";
                }
            }

            else if (body is not null)
                problem = $"{uri.Authority} says nothing of the payment yet.";

            Follow(Id, kept => kept with {
                                   Payment = (kept.Payment ?? new LiveLinkPayment()) is LiveLinkPayment payment
                                                 ? payment with {
                                                       AskedAt      = now,
                                                       Problem      = problem,
                                                       Agreement    = payment.Agreement   ?? agreement,
                                                       AgreementAt  = payment.AgreementAt ?? (agreement is not null ? now : null),
                                                       Receipt      = payment.Receipt     ?? receipt,
                                                       ReceiptAt    = payment.ReceiptAt   ?? (receipt   is not null ? now : null)
                                                   }
                                                 : null
                               });

            if (agreement is not null && follow.Payment?.Agreement is null)
                Log.Info($"The web payment page gave the price agreement of the session {Id} at {follow.PaymentLinkHost}.", "charging", "web");

            if (receipt   is not null && follow.Payment?.Receipt   is null)
                Log.Info($"The web payment page gave the receipt of the session {Id} at {follow.PaymentLinkHost}.", "charging", "web");

        }

        #endregion

        #region (private) CheckPayment(Id)

        /// <summary>
        /// The price agreement and the receipt of a session checked, where they
        /// came and can be checked now, and kept with it: the agreement once
        /// its signed start value held - the session is charged by the tariff
        /// it agreed, where it holds -, the receipt once it ended and its end
        /// value was decided, against what it costs then.
        /// </summary>
        private ChargingSession? CheckPayment(String Id)
        {

            var session = Sessions.Get(Id);

            if (session?.LiveLink is not LiveLinkFollow follow || follow.Payment is not LiveLinkPayment payment)
                return session;

            var now      = TimeProvider.GetUtcNow();
            var checkedP = payment;
            var where    = $"of the session {session.Id} at {session.LocationName}, {session.EVSEId}";

            if (payment.Agreement is not null && payment.AgreementHolds is null)
            {

                var (holds, error, tariff) = SessionCheck.AgreementOf(RoamingNetwork, session, now);

                checkedP = checkedP with { AgreementHolds = holds, AgreementError = error, AgreedTariff = tariff };

                if (holds == true)
                    Log.Notice($"The price agreement {where} holds: its operator signed it for this session and its signed start value, and the tariff {tariff?.TariffId} it agreed - the session is charged by it.",
                               "charging", "web", "poi");

                else if (holds == false)
                    Log.Warning($"The price agreement {where} does not hold: {error}", "charging", "web", "poi");

            }

            var endDecided = follow.EndedAt is not null && (follow.End is null || follow.End.Holds is not null);

            if (payment.Receipt is not null && payment.ReceiptCheck is null && endDecided)
            {

                var check = SessionCheck.ReceiptOf(RoamingNetwork, session with { LiveLink = follow with { Payment = checkedP } }, now);

                checkedP = checkedP with { ReceiptCheck = check };

                Log.Notice($"The receipt {where} says the page took {check.Amount.ToString(CultureInfo.InvariantCulture)} {check.Currency}" +
                           (check.Cost is Decimal cost ? $", and this phone works out {cost.ToString(CultureInfo.InvariantCulture)} {check.Currency}" : "") +
                           (check.Holds switch {
                                true   => ": it holds.",
                                false  => $": it does not hold. {check.Error}",
                                _      => $": not decided. {check.Error}"
                            }),
                           "charging", "web");

            }

            if (checkedP == payment)
                return session;

            return Follow(Id, kept => kept with { Payment = (kept.Payment ?? payment) with {
                                                                AgreementHolds  = checkedP.AgreementHolds,
                                                                AgreementError  = checkedP.AgreementError,
                                                                AgreedTariff    = checkedP.AgreedTariff,
                                                                ReceiptCheck    = checkedP.ReceiptCheck
                                                            } });

        }

        #endregion

        #region (private static) PaymentInWords(Payment)

        /// <summary>
        /// What the payment link of a session said so far, for the log.
        /// </summary>
        private static String PaymentInWords(LiveLinkPayment? Payment)

            => Payment is null
                   ? "nothing yet."
                   : (Payment.Agreement is null ? "no price agreement" : Payment.AgreementHolds switch { true => "a price agreement that holds", false => "a price agreement that does not hold", _ => "a price agreement not checked yet" }) +
                     ", " +
                     (Payment.Receipt is null ? "no receipt" : Payment.ReceiptCheck?.Holds switch { true => "a receipt that holds", false => "a receipt that does not hold", _ => "a receipt not decided" }) +
                     (Payment.Problem is not null ? $" - {Payment.Problem}" : ".");

        #endregion

        #region (private) StartPayingAll() / StopPayingAsync()

        /// <summary>
        /// Ask the payment links of the sessions whose payment is not settled,
        /// from now on - those kept from before a restart as well.
        /// </summary>
        private void StartPayingAll()
        {

            var now = TimeProvider.GetUtcNow();

            foreach (var session in Sessions.All().Where(session => session.LiveLink is LiveLinkFollow { PaymentLink: not null } follow &&
                                                                    !PaymentSettled(follow) &&
                                                                    !(follow.EndedAt is DateTimeOffset ended && now - ended > ReceiptWait && follow.Payment?.Receipt is null)))
                StartPaying(session);

        }

        private async Task StopPayingAsync()
        {

            (CancellationTokenSource Stop, Task Asking)[] followers;

            lock (liveLinksLock)
                followers = [.. paymentFollowers.Values];

            foreach (var follower in followers)
            {
                try
                {
                    await follower.Stop.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // It ended on its own meanwhile.
                }
            }

            await Task.WhenAll(followers.Select(follower => follower.Asking));

        }

        #endregion

    }

}
