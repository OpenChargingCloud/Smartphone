import { nodeAPI,
         request,
         type Certificate        as NodeCertificate,
         type CertificateImport  as NodeCertificateImport,
         type CertificateStore   as NodeCertificateStore,
         type NodeConfiguration,
         type NodeMe,
         type NodeResource,
         type NodeStatus,
         type Operation }  from '@node/api/client';


// What every node answers - the log, name resolution, the time, the store,
// who is signed in - and how it is asked are WWCP_Node's, and every page here
// reads them from this module. What follows is what a smartphone says of its
// own: its configuration's own section, the kinds its store keeps, its map, its
// charging sessions and its account at an e-mobility provider.
export * from '@node/api/client';


/** What a role may be allowed to touch on this smartphone: what every node has, its map, its charging and its account at an e-mobility provider. */
export type Resource = NodeResource | 'map' | 'charging' | 'provider';

/** What somebody signed in to this smartphone may do: an operation on a resource, written "certificates:edit". */
export type Permission = `${Resource}:${Operation}`;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the smartphone is doing right now: what every node says, and nothing more yet. */
export type Status = NodeStatus;

/**
 * What the smartphone is made of: every node's sections, and its own. Only
 * the shape the Configuration page relies on is named; the fields of each
 * section are rendered from whatever the smartphone sends.
 */
export interface Configuration extends NodeConfiguration {
    smartphone:  Record<string, unknown>;
}

/**
 * What a certificate in the store is for: the roots the smartphone believes,
 * the server certificates it holds a server to, who it is as a client, and
 * who its web interface is over HTTPS. No root of clients - nobody connects
 * to a smartphone with a certificate.
 */
export type CertificateKind = 'tlsRoot' | 'tlsServer' | 'tlsIdentity' | 'tlsServerIdentity';

/** One certificate in the store. */
export type Certificate = NodeCertificate<CertificateKind>;

/** What an import into the store sends. */
export type CertificateImport = NodeCertificateImport<CertificateKind>;

/** The store, grouped the way it is shown. */
export type CertificateStore = NodeCertificateStore<CertificateKind>;


/** The status of an EVSE, in OCPI's words. */
export type EVSEStatus = 'AVAILABLE' | 'CHARGING' | 'RESERVED' | 'BLOCKED' | 'INOPERATIVE'
                       | 'OUTOFORDER' | 'PLANNED' | 'REMOVED' | 'UNKNOWN';

/** One connector of an EVSE, as OCPI says it: its power in W. */
export interface Connector {
    id:                  string;
    standard:            string;
    format:              string;
    power_type:          string;
    max_electric_power:  number;
}

/** One charge point of a station, with its status now. */
export interface EVSE {
    uid:          string;
    evse_id:      string;
    /** Of a roaming network: the charging station whose proof says what is signed about it. */
    station_id?:  string;
    status:       EVSEStatus;
    connectors:   Connector[];
}

/**
 * One charging station of the map: an OCPI location, its coordinates as text
 * as OCPI has them, and its price beside it - of a roaming network, only once
 * the proofs of its stations were checked and while they hold.
 */
export interface ChargingLocation {
    id:            string;
    name:          string;
    address:       string;
    postal_code:   string;
    city:          string;
    country:       string;
    coordinates:   { latitude: string; longitude: string };
    operator:      { name: string };
    evses:         EVSE[];
    tariff:        { price_per_kwh: number; currency: string } | null;
    last_updated:  string;
    /** Of a roaming network: whether the proofs of its stations held when last checked; null before. */
    verified?:     boolean | null;
}

/** Where the map takes its stations from: its demo stations, or the roaming network whose trust anchor is set. */
export type MapSource = 'demo' | 'roamingNetwork';

/** Every charging station of the map, and where they came from. */
export interface MapData {
    source:           MapSource;
    stationsURL:      string;
    /** The tile URL template, "https://…/{z}/{x}/{y}.png". */
    tiles:            string;
    timestamp:        string;
    statuses:         EVSEStatus[];
    locations:        ChargingLocation[];
    /** Of a roaming network: which, the URL that listed its pools, or why none did, and how its status is followed. */
    roamingNetwork?:  { id: string; name: string | null; from: string | null; error: string | null; status?: RoamingNetworkStatus | null };
}

/**
 * The status of the charge points of a roaming network, followed over its
 * WebSocket: known while it is reached, from where and since when - or why
 * not. Nothing of it is signed.
 */
export interface RoamingNetworkStatus {
    live:   boolean;
    from:   string | null;
    since:  string | null;
    error:  string | null;
    /** How many EVSEs say their status. */
    evses:  number;
}

/** A public key an energy meter signs its measurements with: the DER of its SubjectPublicKeyInfo as Base64. */
export interface MeterPublicKey {
    /** The SHA-256 of the key, hex. */
    id:                 string;
    algorithm?:         string;
    key:                string;
    certificateChain?:  number;
    error?:             string;
}

/** An energy meter as a proof gives it. */
export interface EnergyMeter {
    id:                string | null;
    manufacturer:      string | null;
    model:             string | null;
    serialNumber:      string | null;
    firmwareVersion:   string | null;
    signatureMethods:  string[];
    publicKeys:        MeterPublicKey[];
}

/** One element of a charging tariff, as OCPI has it. */
export interface TariffElement {
    priceComponents:  { type: string; price: number; vat?: number; stepSize?: number }[];
    restrictions?:    Record<string, unknown>;
}

/** A charging tariff as a proof gives it. */
export interface CheckedTariff {
    id:         string;
    name?:      string | null;
    currency?:  string | null;
    elements?:  TariffElement[];
    /** The EVSE names it, and the proof does not give it. */
    missing?:   boolean;
}

/** An EVSE as a proof gives it. */
export interface CheckedEVSE {
    id:            string;
    maxPowerKW:    number | null;
    currentTypes:  string[];
    connectors:    { id: string | null; type: string | null; format: string | null }[];
    tariffs:       CheckedTariff[];
    /** What charging there costs per kWh: its tariff's, or as a price statement that holds sets it. */
    price:         { pricePerKWh: number; currency: string } | null;
    energyMeter:   EnergyMeter | null;
}

/** A price statement of a station, its pool or one of its EVSEs, whether it holds, and whether it is the one in force. */
export interface CheckedPriceStatement {
    verified:    boolean;
    current:     boolean;
    error:       string | null;
    entityType:  string | null;
    entityId:    string | null;
    signer:      string | null;
    keyId:       string | null;
    sequence:    number | null;
    signedAt:    string | null;
    validFrom:   string | null;
    validUntil:  string | null;
    prices:      { chargingTariffId: string; elements: TariffElement[] }[] | null;
}

/**
 * How the key statement a proof gives fared against the identity keys of the trust anchor trusted here, and - where the
 * proof holds - which holders attest it and until when, at most, what it says holds.
 */
export interface StationTrust {
    signaturesValid:    boolean;
    signersTrusted:     boolean;
    quorumReached:      boolean;
    withinValidity:     boolean;
    trustedHolders:     number;
    requiredConsensus:  number;
    attestedBy:         string[];
    validUntil:         string | null;
}

/**
 * The web payment page of the operator of a station, as its proof gives it - the station's own, else its pool's, else
 * the operator's: where a driver pays ad hoc and starts. Not the URL in the dynamic QR code of the station. Only an
 * https URL is offered; another one is said in its error.
 */
export interface WebPayment {
    url:    string | null;
    of:     'chargingStation' | 'chargingPool' | 'chargingStationOperator';
    error:  string | null;
}

/** The check of one charging station: its proof against the identity keys trusted here, and what it gives where it holds. */
export interface StationCheck {
    stationId:                 string;
    checkedAt:                 string;
    from:                      string | null;
    verified:                  boolean;
    error:                     string | null;
    trust?:                    StationTrust;
    textMismatch?:             string | null;
    warnings?:                 string[];
    roamingNetwork?:           { id: string | null; name: string | null; description: string | null; operator: string | null; revision: number | null; commitId: string | null };
    attestation?:              { role: string; signer: string | null; algorithm: string; keyId: string; signedAt: string; validUntil: string };
    chargingStationOperator?:  { id: string | null; name: string | null };
    station?:                  { name: string | null; manufacturer: string | null; model: string | null; hardwareRevision: string | null; firmwareVersion: string | null };
    webPayment?:               WebPayment | null;
    evses?:                    CheckedEVSE[];
    priceStatements?:          CheckedPriceStatement[];
    priceStatementsError?:     string | null;
    /** Why the statements that hold do not hold together - two of one sequence of an entity - so that none is taken. */
    priceConflict?:            string | null;
}

/** The check of every station of a charging pool of the map. */
export interface LocationCheck {
    locationId:  string;
    name:        string;
    verified:    boolean;
    checkedAt:   string;
    stations:    StationCheck[];
}

/** The roaming network the map takes its stations from, as Configuration → Map shows it. */
export interface RoamingNetworkSettings {
    set:            boolean;
    trustAnchor:    {
        roamingNetworkId:  string;
        name:              string | null;
        description:       string | null;
        urls:              string[];
        /** The thumbprints of the identity keys of its operator, per holder. */
        holders:           string[][];
        minConsensus:      number;
        uri:               string;
        qrCode:            string;
        json:              Record<string, unknown>;
    } | null;
    /** Those of its identity keys trusted here: since when, and by whom. */
    trustedKeys:     TrustedKey[];
    /** How many of its holders have a key trusted here. */
    trustedHolders:  number;
    /** Whether they are as many as the trust anchor requires to agree. */
    quorumTrusted:   boolean;
    maxTextLength:   number;
    file:            string;
}

/** An identity key of the trust anchor trusted here. */
export interface TrustedKey {
    id:          string;
    acceptedAt:  string;
    acceptedBy:  string;
}

/**
 * The key statement of the operator of the roaming network, as the network served it, and how it fares against the
 * trust anchor and the keys trusted here: what is looked at before a key is trusted.
 */
export interface KeyStatementCheck {
    checkedAt:     string;
    from:          string | null;
    error:         string | null;
    keyStatement:  {
        roamingNetworkId:  string;
        operatorId:        string;
        signedAt:          string;
        validUntil:        string;
        minConsensus:      number;
        signedBy:          string[];
        identityKeys:      { id: string; holder: string | null; notBefore: string | null; notAfter: string | null; key: string | null }[];
    } | null;
    evaluation:    {
        signaturesValid:    boolean;
        signersTrusted:     boolean;
        quorumReached:      boolean;
        withinValidity:     boolean;
        canTrustKeys:       boolean;
        trustedHolders:     number;
        requiredConsensus:  number;
        signatures:         number;
        error:              string | null;
    } | null;
}

/** Where the map takes its stations from, as Configuration → Map shows it. */
export interface MapSettings {
    stationsURL:   string;
    source:        string;
    maxURLLength:  number;
    file:          string;
}

/** What a charging session is doing: 'starting' and 'failed' only where the e-mobility provider reports it. */
export type ChargingSessionStatus = 'starting' | 'charging' | 'stopped' | 'finished' | 'failed';

/** What the e-mobility provider reported of a session started through it, the last time it was asked. */
export interface ProviderReport {
    /** The provider's state of the start: sent, started, refused, failed, stopped, ended or expired. */
    state:           string;
    receivedAt:      string;
    /** Whether the charge point operator's charge detail record came. */
    cdr:             boolean;
    /** What the operator's CommandResult of the start said: ACCEPTED, EVSE_OCCUPIED, ... */
    result?:         string;
    /** The status of the operator's session: PENDING, ACTIVE, COMPLETED, INVALID, RESERVATION. */
    sessionStatus?:  string;
}

/** The charging ticket a session was started with: which, good when, and whose keys - nothing secret. */
export interface ChargingTicketReference {
    id:                 string;
    party:              string;
    notBefore:          string;
    notAfter:           string;
    /** The SHA-256 of the ticket key's SubjectPublicKeyInfo, hex. */
    keyThumbprint:      string;
    /** The SHA-256 of the certificate of the provider's ticket issuer, hex. */
    issuerFingerprint:  string;
}

/** What a session keeps of its start through the e-mobility provider, as the provider countersigned it - and of its stop. */
export interface ChargingStartReference {
    /** The OCPI token of this start alone. */
    token:                   string;
    /** The OCPI authorization reference the provider sent the charge point operator. */
    authorizationReference:  string;
    /** The SHA-256 of the start request as the provider countersigned it, hex. */
    requestDigest:           string;
    countersignedAt:         string;
    /** The SHA-256 of the stop request as the provider countersigned it, hex - once there was one. */
    stopRequestDigest?:      string;
    stopCountersignedAt?:    string;
}

/** The account of this phone at an e-mobility provider, as its page shows it: not its password, and not its key. */
export interface ProviderAccount {
    url:           string;
    username:      string;
    email:         string;
    /** Whether the phone made the account, and its password, itself. */
    madeUp:        boolean;
    /** The provider's name for itself in a ticket: "DE*GDF". */
    party:         string;
    since:         string;
    accountKey:    { id: string; subject: string; issuer: string; notBefore: string; notAfter: string };
    ticketIssuer:  { subject: string; fingerprint: string; notAfter: string };
}

/** The account at the e-mobility provider, or none. */
export interface ProviderSettings {
    message?:        string;
    set:             boolean;
    account:         ProviderAccount | null;
    /** How long a ticket of this phone is good for, in minutes. */
    ticketValidity:  number;
    directory:       string;
}

/**
 * One charging session of this phone, as it is now: simulated - with a power,
 * a target and when it is full - or as the e-mobility provider reports it,
 * which tells none of these three.
 */
export interface ChargingSession {
    id:               string;
    status:           ChargingSessionStatus;
    simulated:        boolean;
    location:         { id: string; name: string; address: string; city: string; operator: string };
    evse:             { uid: string; evseId: string; standard: string; powerType: string; maxPowerKW: number };
    startedAt:        string;
    startedBy:        string;
    endedAt:          string | null;
    /** When the charge point operator says charging began, where it reported a session. */
    chargingSince?:   string;
    fullAt?:          string;
    durationSeconds:  number;
    powerKW?:         number;
    energyKWh:        number;
    targetEnergyKWh?: number;
    progressPercent?: number;
    /** What it costs: as the operator reported it, where it did ("reported"), else what its tariff makes of what it charged when ("worked") - or charged times the price, at a demo station. */
    cost:             SessionCost;
    stoppedBy?:       string;
    /** Why this phone stopped it: of a live link, one of its signed values is a wrong one. */
    stoppedBecause?:  string;
    /** What the e-mobility provider reported of it, once it did. */
    provider?:        ProviderReport;
    /** The charging ticket it was started with, where the phone has an e-mobility provider. */
    ticket?:          ChargingTicketReference;
    start?:           ChargingStartReference;
    /** At a station of a roaming network: its meter values and its price, checked against what it kept of the station. */
    check?:           SessionCheck;
    /** Of a session the web payment page of its operator started: its live link, and what came over it. */
    liveLink?:        LiveLinkFollow;
}

/** What a session costs, and how it was worked out. */
export interface SessionCost {
    amount:       number;
    pricePerKWh:  number;
    currency:     string;
    reported?:    boolean;
    /** The tariff it is charged by, where it is charged by one. */
    tariffId?:    string | null;
    /** Whether it is charged by the tariff the operator agreed for it. */
    agreed?:      boolean;
    /** What its tariff makes of what it charged when, part by part. */
    worked?:      TariffCost;
}

/** The cost of a session as its tariff makes it: each part, whether everything could be decided, and why not. */
export interface TariffCost {
    currency:    string;
    amount:      number;
    components:  CostComponent[];
    exact:       boolean;
    notes:       string[];
    /** Whether every part stated its VAT: else its price is taken as it is. */
    vatStated:   boolean;
    /** "minimum" or "maximum", where the least or the most the tariff says made the amount. */
    limit:       'minimum' | 'maximum' | null;
}

/** One part of the cost of a session: of which element of its tariff, what, how much, at which price. */
export interface CostComponent {
    element:    number;
    dimension:  'energy' | 'flat' | 'time' | 'parking';
    quantity:   number;
    unit:       string;
    price:      number;
    vat:        number | null;
    amount:     number;
}

/** The payment of a session as its payment link says it: the price agreement of the operator for it, and the receipt of what the page took. */
export interface LiveLinkPayment {
    askedAt:    string | null;
    problem:    string | null;
    agreement:  { cameAt: string | null; holds: boolean | null; error: string | null; tariffId: string | null; priceStatementDigest: string | null;
                  signer: string | null; signedAt: string | null; keyId: string | null } | null;
    receipt:    { cameAt: string | null; holds: boolean | null; error: string | null; amount?: number; currency?: string; capturedAt?: string;
                  cost?: number | null; signed?: boolean; keyId?: string | null; paymentReference?: string | null } | null;
}

/** The live link of a session the web payment page of its operator started: what came over it, as its signed documents say, and how each signed value held against the proof of the station. */
export interface LiveLinkFollow {
    handedOverAt:   string;
    /** The URL it was fetched from; null where its document itself was handed over. */
    from:           string | null;
    /** The EVSE it names, where it names one. */
    evseId:         string | null;
    /** How to follow it: "https", "httpSSE", "websocket". */
    transports:     string[];
    /** How often something came over it. */
    updates:        number;
    lastUpdateAt:   string | null;
    lastTransport:  string | null;
    lastEndpoint:   string | null;
    /** What ChargyCore made of what came last. */
    lastKind:       string | null;
    /** Why nothing comes now, where nothing does. */
    problem:        string | null;
    endedAt:        string | null;
    endedBecause:   string | null;
    /** How many signed documents it brought. */
    documents:      number;
    /** Its readings, each with whether the document it came in held - null while it was not checked or could not be - and whose key signed it, where it held. */
    readings:       (MeterReading & { holds: boolean | null; signedBy?: SignedBy | null })[];
    energyKWh:      number | null;
    /** How its signed start value held, checked once it came; null while none came. */
    start:          LiveLinkStartCheck | null;
    /** How the signed values after the start held, its end value among them - "notChecked" while one was not checked yet; "byOperator" of those that hold, signed by the operator. */
    values:         { holding: number; byOperator?: number; wrong: number; undecided: number; notChecked: number };
    /** The signed value after the start and before the end that is a wrong one - the phone stopped the session -, or null. */
    wrong:          LiveLinkWrongValue | null;
    /** Whether one of its signed values before the end is a wrong one: the phone stopped the session. */
    refused:        boolean;
    /** How its signed end value held, checked once it came - the whole transaction, where it came so; null while none came. */
    end:            LiveLinkEndCheck | null;
    /** Where the stop link the page gave goes: its host, not the link; null where the page gave none. */
    stopLink:       string | null;
    /** The limit the driver chose at the page, as OCPP 2.1 says it to the station; null where none was handed over. */
    limit:          TransactionLimit | null;
    /** The stop this phone asked the page for last, over its stop link; null while it asked for none. */
    stop:           LiveLinkStop | null;
    /** Each part of its limit against what its signed values say. */
    limits:         LimitValue[];
    /** How it ended, against its limit, once it did; null while it did not, or where the phone stopped it for a wrong value. */
    ending:         LiveLinkEnding | null;
    /** Why the phone disputes it: a signed value is a wrong one, its end value as well, or none signed its end; null where it does not. */
    disputeBecause: string | null;
    /** Where the dispute link the page gave goes: its host, not the link; null where the page gave none. */
    disputeLink:    string | null;
    /** The dispute this phone sent the page last, over its dispute link; null while it sent none. */
    dispute:        LiveLinkDispute | null;
    /** The reference of the payment the page took, which every signed value is to name as what identified the charging ("ID"); null where the page gave none: that its values are of this session is not proven. */
    paymentReference: string | null;
    /** Where the web payment page the proof of the station names is: its host; null where the proof named none. */
    webPaymentHost:   string | null;
    /** Where the hand-over the phone took came from - the host of that web payment page -; null where what the page gave was typed or pasted. */
    handOver:         string | null;
    /** What of what the page gave is bound to the operator of the station: links at the host of its web payment page, a payment reference of a hand-over from there. */
    bound:            { stopLink: boolean; disputeLink: boolean; paymentReference: boolean; paymentLink: boolean };
    /** Where the payment link the page gave goes: its host; null where it gave none. */
    paymentLink:      string | null;
    /** What its payment link said so far; null while it was not asked. */
    payment:          LiveLinkPayment | null;
}

/** What is handed over with a live link, where the web payment page gave it: its stop link, its dispute link, the reference of the payment it took, and the limit chosen there. */
export interface LiveLinkHandOver {
    paymentLink?:       string;
    stopLink?:          string;
    disputeLink?:       string;
    paymentReference?:  string;
    limit?:             TransactionLimit;
}

/** The limit a driver chose at the web payment page, as OCPP 2.1 says it in the "transactionLimit" of RequestStartTransaction. */
export interface TransactionLimit {
    /** At most so much energy, in Wh. */
    maxEnergy?:  number;
    /** At most so much cost, in the currency of the tariff. */
    maxCost?:    number;
    /** At most so long, in seconds. */
    maxTime?:    number;
    /** Up to so full a battery, in percent. */
    maxSoC?:     number;
}

/** A stop the phone asked the web payment page for, over its stop link: whether the page took it, and what it answered. */
export interface LiveLinkStop {
    requestedAt:  string;
    /** The driver, or "this phone" where a signed value is a wrong one. */
    requestedBy:  string;
    accepted:     boolean;
    answer:       string;
}

/** A dispute the phone sent the web payment page, over its dispute link: why, whether the page took it, and what it answered. */
export interface LiveLinkDispute {
    requestedAt:  string;
    /** "this phone", where it sent it once it knew; the driver, asking again. */
    requestedBy:  string;
    reason:       string;
    accepted:     boolean;
    answer:       string;
}

/** One part of the limit of a session against what its signed values say. */
export interface LimitValue {
    kind:     'energy' | 'cost' | 'time' | 'soc';
    /** The limit, in its unit: kWh, the currency of the tariff, seconds or percent. */
    limit:    number;
    unit:     string;
    /** What the signed values say, in its unit; null where they say nothing of it, as of a state of charge. */
    value:    number | null;
    /** Whether it was reached, within a hundredth of it; null where the signed values do not say. */
    reached:  boolean | null;
}

/**
 * How a session followed over its live link ended: stopped by the phone, at its limit, for another reason - or unknown whether at its limit;
 * or its signed end value is a wrong one, and says nothing.
 */
export interface LiveLinkEnding {
    reason:       'stopped' | 'limit' | 'other' | 'unknown' | 'wrong';
    because:      string;
    /** How the meter says the transaction ended ("TX"), or null where it signed no end. */
    transaction:  string | null;
}

/** A signed value of a live link, checked with the key the proof of the station gives for its meter, and against the values before it. */
export interface LiveLinkValueCheck {
    checkedAt:          string;
    /** Whether it holds: false where it is a wrong one - the phone stopped the session -, null where it could not be checked. */
    holds:              boolean | null;
    /** Why it does not hold, or was not checked. */
    error:              string | null;
    meterSerialNumber:  string | null;
    meterId:            string | null;
    /** The key it was checked with: the meter's, or the operator's that signed it. */
    keyId:              string | null;
    /** Whose key its signature is of, where one's is. */
    signedBy?:          SignedBy | null;
    /** The operator, where its key signed it. */
    operatorId?:        string | null;
    /** What ChargyCore said of its signature, where it was asked. */
    result:             string | null;
    /** What it says identified the charging - its "ID": of a web payment, the reference of the payment -, where it was read. */
    identification:     string | null;
}

/**
 * Whose key signed a meter value: its meter's - or, of a value between the start and the end, its operator's, with the
 * key the proof of the station gives it for its charge transparency records.
 */
export type SignedBy = 'meter' | 'operator';

/** The signed start value of a live link, checked. */
export interface LiveLinkStartCheck extends LiveLinkValueCheck {
    /** The reading of the begin of the transaction. */
    reading:            MeterReading;
}

/** A signed value after the start of a live link that is a wrong one, with its readings. */
export interface LiveLinkWrongValue extends LiveLinkValueCheck {
    readings:           MeterReading[];
}

/** The signed end value of a live link, checked, with its readings: the end of the transaction, or all of it. */
export interface LiveLinkEndCheck extends LiveLinkValueCheck {
    readings:           MeterReading[];
}

/** One reading of a signed meter value: when the meter measured it, its register in kWh, "B" or "E" of the transaction. */
export interface MeterReading {
    measuredAt:   string;
    energyKWh:    number | null;
    transaction:  string | null;
    /** "G" where the meter says it is good. */
    status:       string | null;
    /** The errors the meter flags of it: "E" of its energy, "t" of its time. */
    errorFlags?:  string;
}

/** One signed meter value of a charge detail record: whether the meter's key, as the station's proof gave it then, signed it. */
export interface MeterValueCheck {
    nature:             string | null;
    verified:           boolean;
    error:              string | null;
    /** What ChargyCore said of its signature: "ValidSignature", "InvalidSignature", ... - null where no key was found to ask with. */
    result:             string | null;
    meterSerialNumber:  string | null;
    meterId:            string | null;
    keyId:              string | null;
    /** Whose key its signature is of, where one's is. */
    signedBy?:          SignedBy | null;
    /** The operator, where its key signed it. */
    operatorId?:        string | null;
    identification:     string | null;
    /** Whether what it says identified the charging is the token or the ticket of this start. */
    ofThisStart:        boolean;
    readings:           MeterReading[];
}

/** How a session holds, checked under the identity keys trusted now. */
export interface SessionCheck {
    checkedAt:  string;
    /** Why nothing of it was checked. */
    error:      string | null;
    /** Its price, where it was started through an e-mobility provider or by the web payment page of its operator. */
    price?: {
        error:                   string | null;
        /** Whether the price statements are the operator's: "valid", "invalid", "missing". */
        signatures:              string | null;
        /** "unbound": in force for the EVSE at the start, not assigned to the session by the operator; "bound"; "unsupported": assigned, of terms not established; "missing"; "ambiguous"; "invalid". */
        applicability:           string | null;
        /** The price agreement of the operator: "valid", "invalid" - also one of another charging mode than the session's -, "missing". */
        agreement:               string | null;
        publication:             string | null;
        publicationBeforeStart:  string | null;
        chainGaps:               boolean | null;
        customerConsent:         string | null;
        diagnostics:             string[];
        selected: {
            entityType:   string;
            entityId:     string;
            signer:       string;
            sequence:     number;
            signedAt:     string;
            digest:       string;
            tariffId:     string | null;
            currency:     string;
            pricePerKWh:  number | null;
        } | null;
        /** Whether it is the price the phone counted with. */
        matchesSession:          boolean | null;
    };
    /** The signed meter values of its charge detail record, once one came. */
    meterValues?: {
        verified:       boolean;
        error:          string | null;
        values:         MeterValueCheck[];
        startKWh:       number | null;
        endKWh:         number | null;
        energyKWh:      number | null;
        cdrEnergyKWh:   number | null;
        energyMatches:  boolean | null;
        expectedCost:   number | null;
        cdrCost:        number | null;
        costMatches:    boolean | null;
        timeBasis:      string;
    };
}

/** The charging sessions of this phone, the newest first. */
export interface ChargingSessions {
    timestamp:  string;
    simulated:  boolean;
    active:     number;
    total:      number;
    sessions:   ChargingSession[];
}


const session = (id: string) => `/charging/sessions/${encodeURIComponent(id)}`;

/** How the smartphone is asked: the routes every node has, typed with the smartphone's own of them, its map and its charging. */
export const api = {

    ...nodeAPI<{ me: Me; status: Status; configuration: Configuration; kind: CertificateKind; store: CertificateStore }>(),

    map: {
        get:          (refresh = false)                     => request<MapData>        ('GET',  `/map${refresh ? '?refresh=true' : ''}`),
        check:        (locationId: string)                  => request<LocationCheck>  ('GET',  `/map/locations/${encodeURIComponent(locationId)}/check`),
        settings:     ()                                    => request<MapSettings>    ('GET',  '/configuration/map'),
        saveSettings: (settings: { stationsURL: string })   => request<MapSettings>    ('PUT',  '/configuration/map', settings)
    },

    provider: {
        get:          ()                                                              => request<ProviderSettings>('GET',  '/provider'),
        signUp:       (account: { url: string; username: string; email: string })     => request<ProviderSettings>('POST', '/provider/signup', account),
        signIn:       (account: { url: string; username: string; password: string })  => request<ProviderSettings>('POST', '/provider/signin', account),
        forget:       ()                                                              => request<ProviderSettings>('POST', '/provider/forget', {})
    },

    roamingNetwork: {
        settings:     ()                                    => request<RoamingNetworkSettings>('GET',    '/configuration/roamingNetwork'),
        saveSettings: (settings: { trustAnchor: string })   => request<RoamingNetworkSettings>('PUT',    '/configuration/roamingNetwork', settings),
        keyStatement: ()                                    => request<KeyStatementCheck>     ('GET',    '/configuration/roamingNetwork/keyStatement'),
        trustKey:     (keyId: string)                       => request<RoamingNetworkSettings>('PUT',    `/configuration/roamingNetwork/trustedKeys/${encodeURIComponent(keyId)}`),
        distrustKey:  (keyId: string)                       => request<RoamingNetworkSettings>('DELETE', `/configuration/roamingNetwork/trustedKeys/${encodeURIComponent(keyId)}`)
    },

    charging: {
        list:         (onlyActive = false)                  => request<ChargingSessions>('GET',  `/charging/sessions${onlyActive ? '?active=true' : ''}`),
        get:          (id: string)                          => request<ChargingSession> ('GET',  session(id)),
        start:        (locationId: string, evseUid: string) => request<ChargingSession> ('POST', '/charging/sessions', { locationId, evseUid }),
        /**
         * Take over the live link the web payment page of the operator gave - its https URL, or its document - of the session it started there,
         * with the stop link and the dispute link it gave, the reference of the payment it took and the limit the driver chose there, where there are.
         */
        follow:       (locationId: string, evseUid: string, liveLink: string, handOver: LiveLinkHandOver = {}) =>
                          request<ChargingSession>('POST', '/charging/sessions', { locationId, evseUid, liveLink,
                                                                                   ...(handOver.stopLink         !== undefined ? { stopLink:         handOver.stopLink }         : {}),
                                                                                   ...(handOver.disputeLink      !== undefined ? { disputeLink:      handOver.disputeLink }      : {}),
                                                                                   ...(handOver.paymentReference !== undefined ? { paymentReference: handOver.paymentReference } : {}),
                                                                                   ...(handOver.limit            !== undefined ? { limit:            handOver.limit }            : {}),
                                                                                   ...(handOver.paymentLink      !== undefined ? { paymentLink:      handOver.paymentLink }      : {}) }),
        /**
         * Take over the session the web payment page of the operator started there through the hand-over it gave: the https URL of the document
         * that names its live link, its links, the reference of the payment and the limit - taken from the host of that page alone -, or the document.
         */
        takeOver:     (locationId: string, evseUid: string, handOver: string) =>
                          request<ChargingSession>('POST', '/charging/sessions', { locationId, evseUid, handOver }),
        /** Stop a session - of a live link through the stop link of the page: the one handed over, or the one given now. */
        stop:         (id: string, stopLink?: string)       => request<ChargingSession> ('POST', `${session(id)}/stop`, stopLink !== undefined ? { stopLink } : {}),
        /** Ask the payment link of a session of a live link now: its price agreement and its receipt. */
        payment:      (id: string)                          => request<ChargingSession> ('POST', `${session(id)}/payment`, {}),
        /** Dispute a session of a live link through the dispute link of the page - the one handed over, or the one given now - for why the phone disputes it. */
        dispute:      (id: string, disputeLink?: string)    => request<ChargingSession> ('POST', `${session(id)}/dispute`, disputeLink !== undefined ? { disputeLink } : {})
    }

};
