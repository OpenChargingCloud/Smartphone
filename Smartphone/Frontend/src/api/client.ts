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
// own: its configuration's own section, the kinds its store keeps, its map and
// its charging sessions.
export * from '@node/api/client';


/** What a role may be allowed to touch on this smartphone: what every node has, its map and its charging. */
export type Resource = NodeResource | 'map' | 'charging';

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
    /** Of a roaming network: which, the URL that listed its pools, or why none did. */
    roamingNetwork?:  { id: string; name: string | null; from: string | null; error: string | null };
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

/** A price statement of a station, its pool or one of its EVSEs, and whether it holds. */
export interface CheckedPriceStatement {
    verified:    boolean;
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

/** The check of one charging station: its proof against the trust anchor, and what it gives where it holds. */
export interface StationCheck {
    stationId:                 string;
    checkedAt:                 string;
    from:                      string | null;
    verified:                  boolean;
    error:                     string | null;
    textMismatch?:             string | null;
    roamingNetwork?:           { id: string | null; name: string | null; description: string | null; operator: string | null; revision: number | null; commitId: string | null };
    attestation?:              { role: string; signer: string | null; algorithm: string; keyId: string; signedAt: string; validUntil: string };
    chargingStationOperator?:  { id: string | null; name: string | null };
    station?:                  { name: string | null; manufacturer: string | null; model: string | null; hardwareRevision: string | null; firmwareVersion: string | null };
    evses?:                    CheckedEVSE[];
    priceStatements?:          CheckedPriceStatement[];
    priceStatementsError?:     string | null;
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
    maxTextLength:  number;
    file:           string;
}

/** Where the map takes its stations from, as Configuration → Map shows it. */
export interface MapSettings {
    stationsURL:   string;
    source:        string;
    maxURLLength:  number;
    file:          string;
}

/** What a charging session is doing. */
export type ChargingSessionStatus = 'charging' | 'stopped' | 'finished';

/** One charging session of this phone, as it is now. */
export interface ChargingSession {
    id:               string;
    status:           ChargingSessionStatus;
    simulated:        boolean;
    location:         { id: string; name: string; address: string; city: string; operator: string };
    evse:             { uid: string; evseId: string; standard: string; powerType: string; maxPowerKW: number };
    startedAt:        string;
    startedBy:        string;
    endedAt:          string | null;
    fullAt:           string;
    durationSeconds:  number;
    powerKW:          number;
    energyKWh:        number;
    targetEnergyKWh:  number;
    progressPercent:  number;
    cost:             { amount: number; pricePerKWh: number; currency: string };
    stoppedBy?:       string;
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

    roamingNetwork: {
        settings:     ()                                    => request<RoamingNetworkSettings>('GET', '/configuration/roamingNetwork'),
        saveSettings: (settings: { trustAnchor: string })   => request<RoamingNetworkSettings>('PUT', '/configuration/roamingNetwork', settings)
    },

    charging: {
        list:         (onlyActive = false)                  => request<ChargingSessions>('GET',  `/charging/sessions${onlyActive ? '?active=true' : ''}`),
        get:          (id: string)                          => request<ChargingSession> ('GET',  session(id)),
        start:        (locationId: string, evseUid: string) => request<ChargingSession> ('POST', '/charging/sessions', { locationId, evseUid }),
        stop:         (id: string)                          => request<ChargingSession> ('POST', `${session(id)}/stop`, {})
    }

};
