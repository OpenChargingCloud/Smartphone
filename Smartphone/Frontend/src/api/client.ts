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
 * the server certificates it holds a server to, and who it is as a client.
 * No root of clients - nobody connects to a smartphone with a certificate.
 */
export type CertificateKind = 'tlsRoot' | 'tlsServer' | 'tlsIdentity';

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
    uid:         string;
    evse_id:     string;
    status:      EVSEStatus;
    connectors:  Connector[];
}

/** One charging station of the map: an OCPI location, its coordinates as text as OCPI has them, and its price beside it. */
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
    tariff:        { price_per_kwh: number; currency: string };
    last_updated:  string;
}

/** Every charging station of the map, and where they came from. */
export interface MapData {
    /** "demo" until the URL is asked. */
    source:       string;
    stationsURL:  string;
    /** The tile URL template, "https://…/{z}/{x}/{y}.png". */
    tiles:        string;
    timestamp:    string;
    statuses:     EVSEStatus[];
    locations:    ChargingLocation[];
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
        get:          ()                                    => request<MapData>        ('GET',  '/map'),
        settings:     ()                                    => request<MapSettings>    ('GET',  '/configuration/map'),
        saveSettings: (settings: { stationsURL: string })   => request<MapSettings>    ('PUT',  '/configuration/map', settings)
    },

    charging: {
        list:         (onlyActive = false)                  => request<ChargingSessions>('GET',  `/charging/sessions${onlyActive ? '?active=true' : ''}`),
        get:          (id: string)                          => request<ChargingSession> ('GET',  session(id)),
        start:        (locationId: string, evseUid: string) => request<ChargingSession> ('POST', '/charging/sessions', { locationId, evseUid }),
        stop:         (id: string)                          => request<ChargingSession> ('POST', `${session(id)}/stop`, {})
    }

};
