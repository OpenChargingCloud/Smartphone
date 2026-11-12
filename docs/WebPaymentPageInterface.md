# The web payment page and the phone: the interface (draft)

What the **web payment page** of a charge point operator (CPO), its CSMS and
its charging stations have to do so that a driver who pays ad hoc there can
hand the session over to the [Smartphone](../README.md), and the phone can
follow it, check it, stop it and dispute it.

This is the contract for whoever builds the CPO's side. How a session goes
from the driver's point of view, step by step, is
[AdHocChargingAtTheCPO.md](AdHocChargingAtTheCPO.md).

**Status:** this is what the phone does today (November 2026). The hand-over,
the links, their bodies and the payment reference are conventions of this
family, not a standard: no Charge Transparency LiveLink carries them.
The only implementation of the CPO's side is the stand-in of the phone's tests,
[`SmartphoneTests/TestCPO.cs`](../SmartphoneTests/TestCPO.cs). *Must* and
*should* mean what the phone requires and what it recommends.


## What goes where

| | From | To | Over | Required |
|---|---|---|---|---|
| **hand-over** | the page | the phone | the driver, once | no - without it, the driver hands over each of the rest |
| **live link** | the page | the phone | the hand-over, or the driver | yes |
| **signed meter values** | the meter | the phone | the live link | yes |
| **stop link** | the page | the phone | the hand-over, or the driver | no - without it, the driver stops the session at the page |
| **dispute link** | the page | the phone | the hand-over, or the driver | no - without it, the driver disputes the session at the page |
| **payment reference** | the page | the phone, and the meter | the hand-over, or the driver; the CSMS and the station | no - without it, the values are not proven to be of this session |
| **limit** | the page | the phone, and the station | the hand-over, or the driver; OCPP 2.1 | no - without it, nothing is compared at the end |
| **payment link** | the page | the phone | the hand-over, or the driver | no - without it, the price is not bound to the session, and what the page took is not compared |
| **price agreement** | the CPO | the phone | the payment link | no - without it, the price is the one in force, not proven to be this session's |
| **receipt** | the page | the phone | the payment link | no - without it, what the page took is not compared with the cost |
| **stop** | the phone | the page | a POST to the stop link | - |
| **dispute** | the phone | the page | a POST to the dispute link | - |

Where the phone opened the page, the page sends the driver back to it with
the URL of the hand-over, which names all the rest, and the driver copies
nothing ([Sending the driver back](#sending-the-driver-back)). Else the driver
hands over what the page gave in the popup of the station in the phone, at the
charge point paid for: the URL of the hand-over - or, where the page gives
none, the live link, the links, the reference and the limit, each typed or
pasted.

What the phone says a session costs is what the tariff of the station makes of
what its meter signed when - every part of it, see
[7](#7-the-price-and-the-cost).


## 1. What the page gives the driver

### The hand-over

All that follows in one document, which the page gives at an `https` URL of
this session alone - as a link, or as text to copy -, so that the driver hands
over one thing, not five:

```json
{
  "@context":          "https://open.charging.cloud/contexts/wwcp/webPayment/handOver/v1",
  "liveLink":          "https://pay.gef.example/liveLinks/4711",
  "stopLink":          "https://pay.gef.example/sessions/4711/stop?key=9f2c...",
  "disputeLink":       "https://pay.gef.example/sessions/4711/dispute?key=41ab...",
  "paymentReference":  "GEF-2026-11-09-0815",
  "limit":             { "maxEnergy": 20000 },
  "paymentLink":       "https://pay.gef.example/payments/4711?key=77d0..."
}
```

- It must name its `@context` and its `liveLink`: the URL of the live link, or
  the live link document itself. `stopLink`, `disputeLink`,
  `paymentReference`, `limit` and `paymentLink` are each as below, and may be
  left out; what else it says, the phone passes over.
- Its URL must be at the **host of the web payment page** the proof of the
  station names - the `webPaymentURL` of the station, else of its pool, else
  of its operator, as a CPO key that may sign `payments` signed it -, port
  included. The phone asks no other host: a hand-over elsewhere is refused
  before it is asked, and so is one where the proof names no web payment page.
- The phone asks it with a `GET`, `Accept: application/json`, and waits 30
  seconds at most; it must answer `2xx` with the document.
- What it says is then what the page says: its **payment reference** is bound
  to the CPO, and so is every **link at that host** (see
  [Security](#security)). The links should be at that host.
- It gives the stop link and the dispute link to whoever has it: it is a secret
  of the session as they are. The phone keeps its URL, and shows and logs its
  host only.
- The driver may paste the document itself as well. That binds nothing to the
  CPO but the links at the host of the web payment page, as typing them does.

### Sending the driver back

The phone opens the web payment page with one parameter added to its query,
and whatever the `webPaymentURL` has there already left as it is:

```
https://pay.gef.example/stations/S2?handOverTo=https%3A%2F%2Fphone.example%2Fcharging%2FhandOver%3FlocationId%3DDE*GEF*P1%26evseUid%3DDE*GEF*E2*1
```

- `handOverTo` is a URL of the phone, with a query of its own: the charge
  point the driver chose in the phone, in the phone's words, and nothing of
  the driver. The page reads nothing out of it and changes nothing of it.
- Once the session started, the page **sends the browser there** - a `303`, or
  a link the driver follows - with one parameter added to that query:
  `handOver`, the URL of the hand-over, percent-encoded. It adds nothing else,
  and nothing of the payment or the driver.
- It must keep `handOverTo` from the page it was opened with to the one that
  sends the driver back - through the payment service provider and back -, of
  this very payment alone.
- It should send the driver only to an `https` URL without a fragment - or to
  one of the machine itself, for a phone tried there - and should say where,
  by its host, before it does: `handOverTo` is what anybody can put into a
  link to the page, and the URL of the hand-over is a secret of the session
  (see [Security](#security)).
- Opened with no `handOverTo` - by the QR code of a charge point, say -, it
  shows the URL of the hand-over as a link or as text to copy, as before.

The phone takes nothing as the driver arrives: its page says the station, the
charge point and the host of the hand-over, and takes it over once the driver
says so - from the host of the web payment page alone, as a hand-over pasted.

### The live link

A [Charge Transparency LiveLink](https://github.com/OpenChargingCloud/ChargyCore.NET/tree/master/documentation/ChargeTransparencyLiveLink)
document, or the URL of one.

- It must name its `@context`
  (`https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0`),
  when it was `created`, and at least one transport in `liveTransports`:
  `https`, `httpSSE` or `websocket`.
- It should name the charge point, as `chargingStation.EVSE["@id"]`, by the
  EVSE ID the roaming network gives it. The phone refuses a live link that
  names another charge point than the one it is handed over at - and can not
  tell where it names none.
- Its URL must be `https`. The phone asks it with a `GET`, `Accept:
  application/json`, and waits 30 seconds at most; it must answer `2xx` with
  the JSON document.
- Public keys the document carries are not used: every value is checked with
  the key the roaming network attests for the meter (see
  [3](#3-what-the-meter-signs)).

```json
{
  "@context":        "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0",
  "created":         "2026-11-09T10:00:00Z",
  "chargingStation": { "@id": "DE*GEF*S1", "EVSE": { "@id": "DE*GEF*E2*1" } },
  "liveTransports":  [ { "type": "https", "urls": [ "https://pay.gef.example/sessions/4711/live" ], "refresh": 10 } ]
}
```

### The stop link, the dispute link and the payment link

Each an `https` URL of this session alone, query included. Whoever has one
can stop or dispute the session, or read what it was paid: they are secrets of
it (see [Security](#security)). The phone keeps them whole and shows and logs their
host only. Each should be at the host of the web payment page the proof of
the station names: there it is the CPO's, and the session page says so;
elsewhere it says that it is not proven to be. What the payment link answers is
[6](#6-the-payment-link).

### The payment reference

The reference of the payment the page took: a text of 1 to 255 characters, no
control character in it. The station has the meter sign it as the
identification of every value of the session (see
[3](#3-what-the-meter-signs)), and the phone compares it with each value
exactly, character for character.

It is written into every signed meter value and kept with it, so it should be
a reference of this payment alone - not a card number, a name, or anything
else of the driver.

### The limit

The limit the driver chose at the page, as OCPP 2.1 says it in the
`transactionLimit` of `RequestStartTransaction` - and it should be the very
limit the CSMS sends the station:

| Name | Unit |
|---|---|
| `maxEnergy` | Wh |
| `maxCost` | the currency of the tariff |
| `maxTime` | seconds |
| `maxSoC` | percent |

At the end the phone says whether the session ended at its limit - within
1 % of it - or for another reason. A state of charge no meter measures, so of
`maxSoC` it says it can not tell.


## 2. The live link endpoints

What the transports of the live link deliver are the **signed meter values**
of the session, each an OCMF document `OCMF|{payload}|{signature}`, in a form
[ChargyCore.NET](https://github.com/OpenChargingCloud/ChargyCore.NET) reads as
a charge transparency record - the OCMF document itself, as text, as the
stand-in of the tests sends it.

- **`https`:** each request is answered with what the meter signed so far -
  the latest value, or all of them. The phone asks again as `refresh` says, in
  seconds, 10 where it says nothing. Before the first value is signed the
  endpoint may answer `404`, or `2xx` with nothing: the phone asks again after
  1 second, then at doubling intervals up to a minute - so the first value
  may show a minute late. Once it answered, a request that fails is skipped,
  and the next one asked as `refresh` says.
- **`httpSSE`** and **`websocket`:** each value is sent as it is signed. The
  station ends the stream once the meter signed the end of the transaction.
  A stream that ends after something came, without a signed end, is taken as
  ended - and the session is disputed for it (see [5](#5-the-dispute-link)).
- A value sent more than once is kept once. The phone keeps a thousand at
  most: the first, the one of the start, and the latest.
- The **end value** must come over the live link: the reading that ends the
  transaction, alone, or the whole transaction from its begin to its end in one
  document. Over `https` the session ends only with it; the endpoint should
  keep answering with it after the end. Once it came, the phone asks no more.


## 3. What the meter signs

The phone checks every value as it comes - the start value first, then every
value after it, the end value last - with the key the roaming network attests
for the energy meter of that EVSE at the time it was measured, never with a key
the CPO or the live link names. What the station has its meter sign:

| OCMF | Must be | A wrong value where |
|---|---|---|
| `MS` | the serial number the station's proof names for the meter of this EVSE | it names another meter |
| signature | by the meter's own key, as the roaming network attests it for the time `TM` of the reading - the start value and the end value always; a value between them by a key of the operator for its charge transparency records as well (below) | it is signed by another key - a start or an end value by the operator's among them |
| `ID` | the payment reference, in every document of the session, where the page gave one | it names another payment, or none |
| `RD[].ST` | `G`, the meter says the reading is good | another status |
| `RD[].EF` | empty: no error flagged | `E` (energy) or `t` (time) flagged |
| `RD[].RV`, `RU` | an energy register, in `kWh` or `Wh` | none |
| `RD[].TX` | `B` once, at the begin; `E`, `L`, `R`, `A` or `P` once, at the end | a second begin that says another one than the start value |
| `RD[].TM` | in order: nothing before the begin, the register never less later, one value at one time, nothing after the end | it does not fit what held before, or its own readings |

- The **start value** is the first document with a reading `TX` = `B`.
- `IS` and `IT` are not read: they should say what the station's
  authorization says - see [8](#8-ocpp-21-on-the-side-of-the-station).
- **Values between, signed by the operator.** A station may have its meter
  sign the start and the end value only, and sign the values between with a
  key of the operator - as ChargyCore's LiveLink allows. That key is a signing
  key of the CPO in the roaming network, attested in the station's proof, valid
  at the time `TM`, and naming the usage `chargeTransparencyRecords` or none
  (WWCP_POI's `AuthorizedChargingDocumentKeys`); a key for price statements
  alone does not do, nor does one the live link names. Such a value names the
  meter of the EVSE in `MS` as well, and ends nothing: a begin it says again is
  the start value the meter signed. The phone shows it as the operator's, and
  counts it for nothing: what was charged is what the meter signed.
- Where no proof the phone kept attests a key of the meter for a reading's
  time, the value is **not decided**: the phone says so, stops nothing, and
  checks it again - an end value with the proof asked for at the end.

What a wrong value does:

| | Before the end | At the end |
|---|---|---|
| the session | stopped by the phone; its live link followed no more | finished |
| the station | stopped through the stop link | - |
| the page | told through the dispute link | told through the dispute link |
| the value | counts for nothing: energy, cost and limit are what the values that hold say | the same |


## 4. The stop link

```http
POST /sessions/4711/stop HTTP/1.1
Host: pay.gef.example
Content-Type: application/json; charset=utf-8
Accept: application/json

{ "reason": "A signed meter value after its start is a wrong one: The meter counts backwards: 990 kWh at …" }
```

- **The body** is `{}` where the driver stops the session, and
  `{ "reason": "…" }` where the phone stops it for a wrong value - an English
  sentence for people, not a code.
- **`2xx` means taken.** The page then has the CSMS stop the transaction
  (`RequestStopTransaction`), and the meter signs the end, which comes over the
  live link as every value does. Until it comes, the session charges on.
- **Anything else is not taken:** another status - a redirect (`3xx`) as
  well, which is not followed -, no answer within 30 seconds, or no connection.
  The phone then tells the driver to stop the session at the page.
- **The answer** should be a JSON object with a `"message"` (or
  `"description"`, or `"error"`), or plain text: the phone shows the driver
  its first 300 characters, with the status.
- **More than once:** the phone stops a session for a wrong value once - after
  a restart only where it had not sent the stop yet; the driver may ask again
  where the page did not take a stop of theirs. The page should answer a stop
  of a session that is stopped already with `2xx`.
- Nothing but the link authorizes the request: no cookie, no header of the
  driver's.


## 5. The dispute link

The phone disputes a session where

| Case | `reason` begins with |
|---|---|
| its signed start value is a wrong one | `Its signed start meter value is a wrong one:` |
| a signed value after the start is a wrong one | `A signed meter value after its start is a wrong one:` |
| its signed end value is a wrong one | `Its signed end meter value is a wrong one:` |
| the station ended the stream, and no end was signed | `The station ended the stream of its live link, and its meter signed no end of the transaction:` |
| the price agreement does not hold | `The price agreement of its web payment page does not hold:` |
| the receipt does not hold - the page took more than the cost, or another payment | `The receipt of its web payment page does not hold:` |

```http
POST /sessions/4711/dispute HTTP/1.1
Host: pay.gef.example
Content-Type: application/json; charset=utf-8
Accept: application/json

{
  "reason":       "Its signed end meter value is a wrong one: It is of another session: it names \"PAY-0816\" as the identification of the charging (\"ID\"), and the web payment page took the payment \"PAY-0815\" for this session.",
  "signedValues": [ "OCMF|{…the start value…}|{…}", "OCMF|{…the end value…}|{…}" ]
}
```

- **`signedValues`** are the documents exactly as they came: the start
  value, and the one disputed - the wrong one, or the last that came where
  none signed the end, or the end value where the payment is disputed. One
  alone where the start value is the one disputed. The page can check them
  itself: they are its own meter's.
- **`priceAgreement`** or **`receipt`**: where the payment is disputed, what of
  it does not hold, as it came.
- **`reason`** is a sentence for people. Its beginning names the case; what
  follows may change, and should not be parsed.
- **`2xx` means taken**, and everything else not, as at the stop link. The
  phone shows the answer the same way.
- **When:** as soon as the phone knows - for a wrong value before the end once
  it stopped the station, for the end once the session ended, for the payment
  once its agreement or its receipt was checked - and after a restart where it
  was not sent yet. The driver may send it again where the page
  did not take it. So the link must work after the session ended - at least
  until the payment is settled -, and the page must take a dispute it took
  before again, as the same one.
- **What the page does with it** is the CPO's: it should keep it with the
  payment, and not settle the payment as if nothing were disputed - e.g. hold
  the capture of a pre-authorization until it is decided.


## 6. The payment link

An `https` URL of this session alone, which the phone asks with a `GET`,
`Accept: application/json`, 30 seconds at most - from the hand-over on, again
and again, after 5 seconds at first and at doubling pauses up to 10 minutes,
until the receipt came, and no longer than 7 days after the end. The driver can
have it asked at once. It answers `404`, or `2xx` with nothing, until it has
something, then:

```json
{
  "@context":        "https://open.charging.cloud/contexts/wwcp/webPayment/payment/v1",
  "priceAgreement":  { ...the SessionPriceAgreement of WWCP_POI... },
  "receipt":         { ...the receipt... }
}
```

What came first is kept: a later agreement or receipt replaces nothing.

### The price agreement

The `SessionPriceAgreement` of
[WWCP_POI](https://github.com/OpenChargingCloud/WWCP_POI) (experimental), once
the meter signed the start value: the CPO assigns the price to this very
session.

| Field | Must be |
|---|---|
| `roamingNetworkId`, `EVSEId`, `meterSerialNumber` | of this session, as the station's proof says |
| `chargingStarted` | the time `TM` of the reading `TX` = `B` of the start value, to the second it says |
| `startDocumentSHA256` | the SHA-256, as lowercase hex, of the start value exactly as it came over the live link - `OCMF\|{payload}\|{signature}` -, in UTF-8 |
| `chargingMode`, `pricingProfile` | `adhoc`, of the simple profile `chargy-gross-rates/v1` for WWCP_POI to establish its rates: one of `contract` is of a session started through an e-mobility provider, not of this one, and does not hold |
| `chargingTariffId`, `priceStatementDigest` | a tariff of the EVSE and the price statement of its operator in force at the start, of those the phone kept |
| `signer`, `keyId`, `value` | the CPO, signing with a key the proof attests and that may sign `priceStatements`, valid when it signed and at the start |
| `signedAt` | not after now: one signed after the phone's now is checked again later |

The phone checks it once its start value held, with WWCP_POI's
`SessionPriceEvidence`. Where it holds, the session is charged by the tariff it
agreed, and the session page shows its price as bound. Where it does not, the
session is disputed. WWCP_POI establishes the rates agreed of its simple
profile of gross rates (`chargy-gross-rates/v1`): one element without
restrictions, of energy, a flat fee and time each once at most, without VAT -
energy and time in a step size, a flat fee in none. The price of any other
tariff is bound all the same, of a profile WWCP_POI does not establish the
rates of.

### The receipt

What the page took, once it took it:

```json
{
  "@context":          "https://open.charging.cloud/contexts/wwcp/webPayment/receipt/v1",
  "EVSEId":            "DE*GEF*E2*1",
  "paymentReference":  "GEF-2026-11-09-0815",
  "amount":            "14.00",
  "currency":          "EUR",
  "capturedAt":        "2026-11-09T11:02:00.000Z",
  "role":              "chargingStationOperator",
  "signer":            "DE*GEF",
  "signedAt":          "2026-11-09T11:02:00.000Z",
  "algorithm":         "ES256",
  "keyId":             "...",
  "value":             "..."
}
```

- **`amount`** with VAT, as a decimal in a text: a JSON number is written one
  way as a decimal and another as a double, so what is signed would not be one.
- **The signature** is optional, all of `role` to `value` or none. Where given,
  it is over the canonical JSON of the receipt but its `value`, as WWCP_POI
  signs a `SessionPriceAgreement`, by the CPO with a key the proof attests and
  that may sign `payments`. Then the receipt is evidence of what was taken;
  unsigned, it is the page's word.
- **The phone checks it** once the session ended and its end value was decided:
  of this EVSE, of the payment reference handed over where both name one, in
  the currency of the tariff, signed by the CPO where signed - and its amount
  not more than the cost the phone works out (see [7](#7-the-price-and-the-cost)).
  A receipt of more, or one that does not hold otherwise, disputes the session.
  Where the cost could not be worked out exactly, nothing is decided.


## 7. The price and the cost

What a session costs, the phone works out of the tariff it is charged by - the
one agreed for it, else the first of the EVSE the proof names, with its
elements as the price statement in force at the start sets them - and of what
its meter signed when, as OCPI prices a session:

- **Every part:** `ENERGY` per kWh, `FLAT` once, `TIME` per hour charging,
  `PARKING_TIME` per hour not charging - between two signed values whose
  register did not grow. For each, at every time, the first element whose
  restrictions hold then that prices it; a flat fee of the first that holds
  at the start.
- **Restrictions:** times of day, dates and days of the week of the time zone
  of the pool of the station (`timeZone`: an IANA or Windows id, or an offset
  as `UTC+01:00`); energy and duration of the session so far; power between two
  signed values. A restriction of current - and a component of current - no
  meter value says: an element with it is taken as one that does not hold, and
  the cost is said to be not exact. An element of a reservation prices none.
- **Step sizes** round each part up; **VAT** is added where a component states
  it - else its price is taken as it is -, and the **least and the most** the
  tariff says hold the whole.

The limit of the driver's cost, the receipt and the cost a charge detail record
says are compared with that.


## 8. OCPP 2.1 on the side of the station

What the phone checks asks this of the CSMS and the station. How they do it is
theirs; this is the way OCPP 2.1 offers. Whether the CSMS and the charging
station of this family do it is not checked here.

1. **Start:** `RequestStartTransaction` with
   - the limit as its `transactionLimit`, and
   - an `idToken` of type `Central` - an id generated centrally, as for a
     remote start - whose value is the payment reference. OCPP 2.1 does the
     same where a payment terminal at the station took the payment: its
     `NotifySettlementRequest` says the payment reference is used as the value
     for the `idToken`.
2. **The station** writes the `idToken` of the transaction into the
   identification of every meter value it has signed for it - OCMF `ID`, with
   `IS` and `IT` as for any authorization -, and sends each value over the
   live link.
3. **Stop:** `RequestStopTransaction`, where the page took a stop; the station
   ends the transaction at its limit itself.
4. **The dynamic QR code:** `NotifyWebPaymentStarted`, where the payment
   started at the page the QR code of the EVSE led to.


## Security

- **The links are keys.** A stop link stops the session, a dispute link
  disputes it, a payment link tells what was paid, for anybody: each should
  hold at least 128 random bits, be of one session alone, be `https`, and stop
  working once the payment is settled - the dispute link and the payment link
  not before.
- **The price agreement and a signed receipt are signed by the CPO**, with keys
  the roaming network attests: they bind the price to the session and what was
  taken to the CPO, wherever they came from.
- **The host of the web payment page binds the hand-over to the CPO.** That
  host is in the station's proof, signed by a CPO key that may sign
  `payments`, and TLS proves who answers there: a hand-over is taken from that
  host alone, its payment reference is the page's, and a stop or dispute link
  at that host is the CPO's - the session page says which are. Nothing of the
  hand-over itself is signed.
- **`handOverTo` is the word of whoever linked the page.** A link to the page
  with somebody else's `handOverTo` has the driver who pays there sent to that
  somebody, with the URL of the hand-over - and so with the stop, dispute and
  payment links of the session. So the page says where it sends the driver,
  sends nobody anywhere but to `https`, and adds the hand-over alone. On the
  phone's side nothing is taken for an address opened: the driver is asked,
  and a hand-over at another host than that of the web payment page is
  refused, whoever sent the driver.
- **The rest is the word of whoever gave it:** links at another host, and a
  reference typed or pasted. A forged stop link that answers `2xx` makes the
  phone believe the station stops; the phone sees only that no end comes.
- **The values are bound regardless:** the live link to the charge point it
  names, every value to the key the roaming network attests, and - by the
  payment reference - to the payment.
- **The payment reference is public** to whoever sees the meter values: it is
  no secret, and no personal data should be in it.


## Testing a page against the phone

The stand-in of the tests, [`TestCPO.cs`](../SmartphoneTests/TestCPO.cs), is
the smallest page the phone works with:

| | |
|---|---|
| `GET /liveLinks/{session}` | the live link document, `application/json` |
| `GET /sessions/{session}/live` | the latest signed value, `text/plain`; `404` before the first |
| `POST /sessions/{session}/stop` | `200` `{ "message": "The station stops." }`, or what a test sets |
| `POST /sessions/{session}/dispute` | `200` `{ "message": "The dispute is taken." }`, or what a test sets |
| `GET /handOvers/{session}` | the hand-over: the live link, the stop and dispute links above, the payment reference `payment-{session}`, a limit of 20 kWh and the payment link below, `application/json` |
| `GET /payments/{session}` | the payment: the price agreement and the receipt a test sets, `application/json`; `404` before |
| `GET /pay?session={session}&handOverTo={url}` | the page once the driver paid: `303` to `handOverTo` with `handOver` added, the URL of the hand-over; `400` where `handOverTo` is neither `https` nor of the machine itself, or has a fragment; with no `handOverTo`, the URL of the hand-over as `text/plain` |

The tests give the DC station the stand-in's own URL as its web payment page,
so that its hand-over is taken from there, and a test may price its EVSE as it
likes - a flat fee and energy, say - and sign a price agreement and a receipt
as DE*GEF. The stand-in serves `http` on the
loopback, which the phone allows there alone.

A real page can be tried the same way: on a phone that trusts the roaming
network of the station, hand its hand-over over at the charge point paid for -
or its live link, links and reference -, and watch the session's page: where
the hand-over came from and what of it is the CPO's, what held, what was
stopped, what was disputed and what the page answered.


## Open points

- **The page sends the driver back only where the phone opened it.** Opened
  by the QR code of a charge point it knows no `handOverTo`, and the driver
  copies the URL of the hand-over. An address every app answers at - an
  Android App Link, an iOS Universal Link - would spare that, or a QR code of
  that URL the phone scans; there is neither.
- **`handOverTo` is not bound to the phone** that opened the page: the page
  cannot tell it from one in a link somebody else made (see
  [Security](#security)).
- **Links at another host** than that of the web payment page are not bound to
  the CPO, nor is anything typed or pasted. Binding them would take the CPO
  signing the hand-over with a key the roaming network attests for `payments`.
- **The answer to a dispute has no form:** no case number, no place to ask how
  it is decided.
- **A dispute the page did not take** is sent again only where the driver asks,
  or after a restart where it was never sent.
- **The payment reference** is handed over with the live link only, not later
  as the links can be: values that held before it came would have to be judged
  anew.
- **Values signed by the operator** between the meter's are taken with a key
  of the CPO for `chargeTransparencyRecords` - WWCP_POI has no usage for meter
  values alone, so that key signs the CPO's whole documents as well - and of
  P-256 only.
- **A session disputed for a wrong value** is not disputed again for its
  payment: its dispute stands, and the page has the values it disputes.
- **The first price agreement and receipt are kept:** a page that corrects one
  replaces nothing; the session is disputed for the first.
- **A component or a restriction of current** is priced at nothing: no meter
  value says the current.
