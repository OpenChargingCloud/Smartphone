# Starting a charging session with a charging ticket (draft)

How a driver's app starts and stops a charging session at a station of a
roaming network through its e-mobility provider (EMSP), so that

- the charge point operator (CPO) learns nothing of who the driver is, nor
  that two sessions were the same driver's;
- nobody but the driver's app can start a session on the driver's ticket -
  not a man in the middle, and not the EMSP at another charge point or at
  another time;
- the EMSP still says, in a way the CPO can keep, that it vouches for the
  start and pays for it;
- the driver can later show which signed meter values were theirs, without
  showing their account.

A draft for the Smartphone (the app), the
[EMSP](https://github.com/OpenChargingCloud/EMSP) and a CSMS behind OCPI.
What exists today and what does not is said at the end.


## The parties and their keys

| Who | Key | What it says | Who trusts it, and how |
|---|---|---|---|
| the driver's account at the EMSP | an **account key**, P-256, long-lived, one or more per account | "this is me" | the EMSP: it certified it below its account CA, from a PKCS#10 request the app made (`POST /api/v1/account-keys`) |
| one charging session | a **ticket key**, P-256, made for one start and thrown away after it | "I hold this ticket" | the CPO: the ticket names it, and the EMSP signed the ticket |
| the EMSP | its **ticket issuer key** | "this EMSP issued this ticket", and "this EMSP vouches for this start" | the CPO: by the issuer certificate (`GET /api/v1/tickets/issuer.pem`), or by a signing key of the EMSP as a party of the roaming network (WWCP_POI, `eMobilityProviders`) |
| the roaming network's operator | its identity keys, by their thumbprints | what the network says of the station: its EVSEs, tariffs, energy meters and their keys | the app: by the trust anchor it was given |

The account key never reaches the CPO. The ticket key never reaches anybody
but the app.


## 1. Before: the station, checked

The app has checked the proof of the station against the trust anchor of the
roaming network (Smartphone, map of a roaming network): it knows the EVSE, its
tariff and price, and the public key its energy meter signs meter values with.
It keeps the proof - the digest of its effective tree and its commit - so that
the start request can name what the driver saw.


## 2. The ticket: one per start

Exactly as the EMSP issues tickets today (EMSP README, "Account keys and
charging tickets"):

1. The app makes a ticket key for this start alone.
2. It writes the payload - deterministic CBOR, protected body header
   `{3: "application/charging-ticket+cbor"}`:
   ```
   { "typ": "ChargingTicket", "v": 1, "id": h'16 random bytes', "emsp": "DE*GDF",
     "key": <COSE_Key of the ticket key>, "nbf": <now>, "exp": <now + 1 h>,
     "limits": { "kW": .., "minutes": .., "kWh": .. } }
   ```
3. It signs it twice with ES256 - with the ticket key (kid `"ticket"`) and
   with an account key (kid the SHA-256 of its certificate) - and sends it,
   `POST /api/v1/tickets`.
4. The EMSP checks both signatures, takes the account key's signature off,
   signs with its ticket issuer key, and answers with the ticket signed by
   the ticket key and by itself.

**One ticket per start.** A ticket that is used twice links two sessions at
the CPO. The EMSP takes tickets good for up to 30 days; the app asks for one
good for an hour, made right before the start.

**Nothing that tells drivers apart.** `nbf` is now, `exp` a fixed time after
it, and the limits are the same few values for everybody (or none): a ticket
with unusual limits is a fingerprint of its own.


## 3. The start request: signed by the ticket key, and by the EMSP

A **start request** is a COSE_Sign (CBOR tag 98) with the protected body header
`{3: "application/start-request+cbor"}` and the payload, written by the app:

```
{
  "typ":     "StartRequest",
  "v":       1,
  "ticket":  h'SHA-256 of the ticket as the EMSP issued it (its COSE_Sign bytes)',
  "evse":    "DE*GEF*E2*1",                 ; the EVSE, as the roaming network names it
  "at":      1793200000,                    ; seconds since 1970, when the driver asked
  "nonce":   h'16 random bytes',
  "proof":   h'the effectiveTree digest of the version the app checked',   ; optional
  "tariff":  "DE*GEF*T_DC"                                                  ; optional
}
```

It carries **two signatures over the same payload**:

1. **The ticket key's** (kid `"ticket"`), made by the app. It says: the
   holder of this ticket wants to charge at exactly this EVSE, now. The EMSP
   cannot change the EVSE, the time or the ticket without breaking it; nor
   start on the ticket somewhere else; nor start again with the same request,
   which the nonce makes once-only.

2. **The EMSP's** (kid the SHA-256 of its ticket issuer certificate), added by
   the EMSP. It says: this EMSP issued this ticket to an account it knows, has
   not withdrawn it, vouches for this start and pays for it. What only the
   EMSP sets goes into the **protected header of its own signature**, so that
   it is covered by the EMSP's signature alone and the app's signature stays
   valid:

   ```
   { 1: -7,                                   ; ES256
     4: h'SHA-256 of the issuer certificate',  ; kid
     "token":    "T-9F2C41D08A7E3B65",         ; the OCPI token of this start alone
     "authRef":  "8e1c…",                      ; OCPI authorization_reference
     "iat":      1793200002 }                  ; when the EMSP signed
   ```

   COSE's `Sig_structure` covers the body's protected header, the signer's
   protected header and the payload: the app's signature covers what the app
   wrote, the EMSP's what the app wrote and what the EMSP added.

The app sends the request to the EMSP **with the ticket** it names:
`POST /api/v1/tickets/{ticketId}/start` with
`{"ticket": "<base64>", "request": "<base64>"}`. ES256 signatures are
randomized, so the digest of an issued ticket is the one of exactly the bytes
the EMSP sent back - which the app holds and the EMSP does not keep: its
record of a ticket is its metadata. With the ticket at hand the EMSP checks the
digest against it rather than against a copy of its own.

**The EMSP checks** that the ticket carries its own issuer's signature and is
one it issued to the account that asks (by its `id`), is good now and not
withdrawn; that the request's `ticket` is the digest of these bytes, and the
request is signed by the ticket's key; that `at` is within a few minutes of its
clock; that the nonce is new; that the EVSE is one of a CPO it is peered with.
It adds its signature and sends the command.

**The EMSP answers** once the CPO has accepted the command (OCPI's
synchronous `CommandResponse`, `ACCEPTED`): `200` with the request as it
countersigned it, both signatures:

```json
{ "request": "<base64 of the start request, both signatures>",
  "token":   "T-9F2C41D08A7E3B65",
  "authRef": "8e1c…" }
```

Its `token` and `authRef` are for reading only: what counts is what the
protected header of its signature says. Anything else - a ticket it did not
issue, a request that does not hold, a CPO that rejects the command - is an
error status with `{"error": "…"}`, and no countersigned request.

**The app checks** what comes back before it calls anything started: the
body's protected header, the payload and its own signature are the bytes it
sent; there is exactly one more signature, its kid the SHA-256 of the ticket
issuer certificate the app was given when it signed in, and it holds for that
certificate's key; its protected header - not its unprotected one - says
`token`, `authRef` and `iat`, and `iat` is within five minutes of the app's
clock. A start that fails any of it is refused, and nothing is started. The
app keeps the countersigned request with the ticket and its key: it is what
the stop names, and what proves the driver asked for this start.


## 4. EMSP → CPO: OCPI `START_SESSION`

```json
{
  "response_url":            "https://emsp.example/ocpi/2.2.1/commands/START_SESSION/8e1c…",
  "token": {
    "country_code": "DE", "party_id": "GDF",
    "uid":          "T-9F2C41D08A7E3B65",
    "type":         "APP_USER",
    "contract_id":  "T-9F2C41D08A7E3B65",
    "issuer":       "GraphDefined EMSP",
    "valid":        true,
    "whitelist":    "NEVER",
    "last_updated": "…"
  },
  "location_id":             "DE*GEF*P1",
  "evse_uid":                "DE*GEF*E2*1",
  "authorization_reference": "8e1c…",
  "charging_ticket":         "<base64 of the ticket>",
  "start_request":           "<base64 of the start request, both signatures>"
}
```

**The token is of this start alone.** Its `uid` and `contract_id` are new for
every start and say nothing of the account - not a card's UID, not an eMAID.
Were it the driver's card or contract, every session would be linked at the
CPO again, ticket or not. The EMSP keeps which account a token was made for,
for the CDR.

`charging_ticket` and `start_request` are fields OCPI does not know; a
receiver that does not know them ignores them and treats the command as any
other `START_SESSION` - which is why the token must be one the CPO may ask
about (`whitelist` `NEVER`: it asks the EMSP in real time, and the EMSP says
`ALLOWED` for exactly this start).

So the EMSP's real-time authorization knows **tokens of one start**, a path of
their own beside its cards and its eMAIDs: a token made for a start, `ALLOWED`
for the location and EVSE of that start and while its ticket is good, and for
nothing else; `BLOCKED` once the session it was made for has ended.


## 5. The CPO checks

A CPO that knows charging tickets checks, before it starts:

1. the ticket: signed by its own key and by an issuer it trusts, for this
   EMSP (`emsp` and the OCPI party), good now;
2. the start request: its `ticket` the digest of this ticket, signed by the
   ticket's key; its `evse` the EVSE of the command; its `at` within a few
   minutes; its nonce not seen before;
3. the EMSP's signature on the start request, by the issuer it trusts, its
   `token` the command's token and its `authRef` the command's
   `authorization_reference`;
4. the ticket's limits, which become the station's: power, duration and
   energy, e.g. as a charging profile of OCPP's `RequestStartTransaction`.

It keeps the ticket and the start request with the session: they are its
evidence that the EMSP authorized and the driver asked for this start, at
this EVSE, at this time - whatever the EMSP says later.


## 6. The meter values: the driver's, without the driver's name

The station starts the transaction with the token as its `idToken`. Where its
energy meter signs OCMF, the identification fields of the meter values carry
what identifies the ticket and nothing else - e.g. `IS` true, `IT`
`UNDEFINED`, `ID` the token or the ticket's id.

The app checks the meter values with the key of the energy meter the proof of
the station gave before the start (step 1), and holds the ticket: it can show
that these signed meter values were of its session - to the EMSP, or anybody
disputing an invoice - without showing its account.


## 7. Stopping

A **stop request** is a start request's twin, with the protected body header
`{3: "application/stop-request+cbor"}` and the payload

```
{
  "typ":     "StopRequest",
  "v":       1,
  "ticket":  h'SHA-256 of the ticket as the EMSP issued it',
  "start":   h'SHA-256 of the start request as the EMSP countersigned it',
  "at":      1793203600,
  "nonce":   h'16 random bytes'
}
```

The start is named by the bytes the CPO holds of it, which name the session
before the CPO has given it an id. Signed by the ticket key, the stop goes to
the EMSP with the ticket and the start it names, as neither is kept there:
`POST /api/v1/tickets/{ticketId}/stop` with
`{"ticket": "<base64>", "start": "<base64>", "request": "<base64>"}`.

The EMSP checks the ticket and the request as for a start, and that the start
is one it countersigned for this account and not stopped yet; it
countersigns with **the token and the `authRef` of that start** and its own
`iat`, sends OCPI `STOP_SESSION`, and answers `{"request": "<base64>"}`. The
app checks the countersignature as it does a start's, and that its token and
reference are the start's. Only the holder of the ticket stops; the EMSP can
still stop on its own, as OCPI lets it, and says so by its signature alone.

A stop that does not hold leaves the session charging: the app says so, and
the next stop that holds ends it. While a session started through the EMSP
charges, the app does not forget its account there - only the account can
stop it.


## 8. The CDR

The CDR comes back to the EMSP with the start's token (`cdr_token`); the EMSP
knows its account and bills it. The CPO's CDR names no account.


## 9. After the start: what the app learns of it

The EMSP's `200` says that the CPO took the command, not that anything
charges. What became of the start reaches the EMSP afterwards, from the CPO:
OCPI's asynchronous `CommandResult` at the command's `response_url`, the
session the CPO pushes and updates, and at its end the CDR. The app asks the
EMSP for all of it by the ticket, signed in as the account the ticket was
issued to: `GET /api/v1/tickets/{ticketId}`. For any other account, and for a
ticket the EMSP did not issue, the answer is `404`, the same for both.

```json
{ "id":        "3f6c…",
  "notBefore": "2026-10-29T12:00:00Z",
  "notAfter":  "2026-10-29T13:00:00Z",
  "starts": [
    { "start":      "<SHA-256 of the start request as the EMSP countersigned it, lower-case hex>",
      "token":      "T-9F2C41D08A7E3B65",
      "authRef":    "8e1c…",
      "evse":       "DE*GEF*E2*1",
      "state":      "started",
      "startedAt":  "2026-10-29T12:00:02Z",
      "endedAt":    null,
      "result":     { "result": "ACCEPTED", "message": [], "receivedAt": "2026-10-29T12:00:09Z" },
      "stopResult": null,
      "session":    { "id": "S1", "status": "ACTIVE", "kwh": 4.2, "…": "…" },
      "cdr":        null }
  ] }
```

- **`starts`** are the starts made with this ticket, the newest first: one,
  where the app keeps to one ticket per start. `evse` is the EVSE as the
  start request names it, for showing: what binds the start to it is the
  request, which `start` names.
- **`state`** is the EMSP's own:
  - `sent`: countersigned, the command on its way;
  - `started`: the CPO took the command;
  - `refused`: it did not take it;
  - `failed`: its `CommandResult` said anything but `ACCEPTED`;
  - `stopped`: stopped by the holder of the ticket;
  - `ended`: the CPO reported the session `COMPLETED` or `INVALID`;
  - `expired`: the ticket ran out first.

  From `refused` on, the token is `BLOCKED`, and `endedAt` says since when.
- **`result`** and **`stopResult`** are the `CommandResult`s of
  `START_SESSION` and `STOP_SESSION` as the CPO sent them, and when the EMSP
  took them. Each is absent until the CPO sent it. The EMSP keeps them with
  the start, so that they outlast a restart.
- **`session`** and **`cdr`** are what the CPO pushed for the start's
  `authorization_reference`, as OCPI writes them, as last pushed. Each is
  absent until the CPO pushed it. Of a session the app reads `status`,
  `start_date_time`, `end_date_time`, `kwh`, `total_cost` and `currency`; of a
  CDR its times, `total_energy`, `total_cost`, `currency` and `signed_data`.

**The app asks** while a session started through the EMSP is shown and not
over: when its pages ask for it, at most every 15 seconds. Once the session is
over, it asks on in the same way until the CDR came - and no longer than a
day after the ticket ran out. It takes a start of the
answer only where `start`, `token` and `authRef` are those of the start it
keeps. It takes a session or a CDR only where its `authorization_reference` is
the start's and its `cdr_token.uid` is the start's token.

It keeps the last answer it took with the session. From the first one on,
what the app shows is what the EMSP says, and nothing simulated:

| the EMSP says | the app shows |
|---|---|
| no session yet, `sent` or `started` | starting |
| a session `PENDING` or `RESERVATION` | starting |
| a session `ACTIVE` | charging, with its `kwh` and `total_cost` |
| a session `COMPLETED`, or a CDR | stopped where the app stopped it, else finished, with the CDR's figures once it came |
| `failed`, or a session `INVALID` | failed, with the `CommandResult`'s `result` |
| `expired` without a session | failed: the CPO never reported one |

An EMSP that answers `404` or `405` to the request knows nothing of the
start, or does not answer it yet. For such an EMSP the app simulates the
figures, as before, and says so.

The meter values are the CDR's `signed_data`. The app checks them against
the key of the energy meter that the station's proof gave (step 6) in a later
step: until then it keeps them, and shows the CDR's figures as the CPO's.


## What it hides, and what it does not

- **From the CPO:** who the driver is, and that two sessions were the same
  driver's - as long as each start has a ticket and a token of its own, and
  the ticket says nothing that tells drivers apart.
- **Not from the CPO:** the EVSE, the time, the energy; at ISO 15118 the
  vehicle's EVCCID; the IP address of an app that talks to the station
  directly. A ticket hides the account, not the car.
- **Not from the EMSP:** whose ticket it is. It has to know, to bill and to
  withdraw a ticket that is misused. Hiding it from the EMSP as well takes
  blind signatures (e.g. RFC 9474) and prepaid tickets - not this draft.


## One CBOR, one COSE

The app, the EMSP and the CSMS write and check all of it with the CBOR and
COSE of [Styx](https://github.com/Vanaheimr/Styx) (`org.GraphDefined.Vanaheimr.Illias`,
below Hermod), and with nothing of their own:

| | Styx |
|---|---|
| the ticket's and the start request's payload, deterministic | `CBORWriter` / `CBORValue` |
| the ticket key in the payload, a COSE_Key on P-256 | `COSEKey` (EC2), checked to be on the curve |
| whose key a signature is: its kid, in the signature's protected header | the ticket key's `"ticket"`; an account key's and the ticket issuer's the SHA-256 of its certificate, as the EMSP names them - not a COSE Key Thumbprint (RFC 9679) |
| the ticket and the start request, one payload with several signers | `COSESign` (tag 98), `COSESignature` |
| the app's signature, made first | `COSESign.Sign(payload, ticketKey, protectedHeader, …)` |
| the EMSP's signature, added without touching the app's | `COSESign.AddSignature(issuerKey, protectedHeader)`, its own fields in its own protected bucket (`COSEHeaders`, text labels) |
| every check | `COSESign.Verify(signature, publicKey, …)`, per signature |

The EMSP's own `Tickets/COSE.cs` on `System.Formats.Cbor` goes, and the
tickets it issues today are read and written with Styx, the bytes unchanged:
the payload is deterministic CBOR either way, and a COSE_Sign is the same
four-element array.

**Protected headers are checked as the bytes that came, never rebuilt.** Styx
writes a protected header in the order its parameters were added
(`COSEHeaders.ToProtectedByteArray`), not deterministically: every signature
covers the bytes as they travel with the message, so nothing changes for
whoever checks those - but a header written anew from its values may be other
bytes, and its signature then no longer holds. Payloads are deterministic CBOR
(RFC 8949, section 4.2.1); a map with a key twice is refused.


## Open points

- **OCPI.** `charging_ticket` and `start_request` as fields of
  `START_SESSION` / `STOP_SESSION`, or a module of their own; and through a
  roaming hub, which has to pass them on.
- **Trusting the EMSP.** Its ticket issuer certificate per OCPI peering, or
  its signing keys as a party of the roaming network in WWCP_POI - so that a
  CPO trusts every EMSP of the network by the same trust anchor the app uses.
- **Header labels.** Text labels (`"token"`, `"authRef"`, `"iat"`) in the
  EMSP's protected header, or registered integers.
- **Clocks.** How many minutes `at` may be off, at the EMSP and at the CPO;
  the app takes five for the EMSP's `iat`.
- **After the start.** The app asks the EMSP, as section 9 says, and is not
  told: whether the EMSP should push it, and how. How the app checks the
  CDR's signed meter values against the station's energy meter.
- **Offline.** A station that checks tickets itself, without its CSMS.


## What exists today

| | |
|---|---|
| EMSP | self sign-up; account keys from a PKCS#10 request; tickets issued from a request signed by the ticket key and an account key, the account's signature taken off - with Styx's CBOR and COSE in place of its own; start and stop requests (`POST /api/v1/tickets/{id}/start` and `/stop`) checked and countersigned with a token per start - `token`, `authRef` and `iat` as text labels - and sent on as OCPI `START_SESSION` / `STOP_SESSION` on 2.2.1 and 2.3.0; the token `ALLOWED` while its ticket is good, and only at the location and EVSE of its start and for the CPO it went to - through WWCP_OCPI's `OnLocalTokenAuthorization` - and `BLOCKED` once its session is over |
| EMSP, missing | what became of a start, told as section 9 says (`GET /api/v1/tickets/{id}`) |
| CSMS / CPO, missing | checking a ticket and a start request, limits as a charging profile, the token in the OCMF identification |
| Smartphone | the station's proof checked against the trust anchor, its tariff, and its energy meter's key; an account at an EMSP, signed up or in with a session; an account key from a PKCS#10 request; a ticket per start, built, signed and checked with Styx - against the EMSP as committed and as moved to Styx; start and stop requests, signed by the ticket key and their countersignature checked - against a stand-in EMSP of its tests, which countersigns as this draft says; against the EMSP as far as it goes without a CPO, ticket and start request taken and the EVSE unknown to it; what became of a start, asked for as section 9 says and taken only where it is of that start - the session as the stand-in tells it, simulated where a provider tells nothing |
| Smartphone, missing | the signed meter values of a charge detail record checked against the energy meter's key: kept as they came, not checked yet |
