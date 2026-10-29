/*
 * What the tests of the map and the charging pages are given to draw: two
 * stations as a smartphone sends them, and a charging session as it sends one;
 * and a charging pool of a roaming network, with the check of its stations.
 * Each test lays over them what it is about.
 */

import type { ChargingLocation, ChargingSession, LocationCheck, MapData, StationCheck } from '../src/api/client';


/** A station of the map: one available AC point and one out of order. */
export function holzmarkt(more: Partial<ChargingLocation> = {}): ChargingLocation {
    return {
        id:            'DEMO-JENA-HOLZMARKT',
        name:          'Jena Holzmarkt',
        address:       'Holzmarkt 1',
        postal_code:   '07743',
        city:          'Jena',
        country:       'DEU',
        coordinates:   { latitude: '50.928420', longitude: '11.587610' },
        operator:      { name: 'Saaletal Strom (demo)' },
        evses:         [
            { uid: '1', evse_id: 'DE*SST*E2001*1', status: 'AVAILABLE',
              connectors: [ { id: '1', standard: 'IEC_62196_T2', format: 'SOCKET', power_type: 'AC_3_PHASE', max_electric_power: 22000 } ] },
            { uid: '2', evse_id: 'DE*SST*E2001*2', status: 'OUTOFORDER',
              connectors: [ { id: '1', standard: 'IEC_62196_T2', format: 'SOCKET', power_type: 'AC_3_PHASE', max_electric_power: 22000 } ] }
        ],
        tariff:        { price_per_kwh: 0.49, currency: 'EUR' },
        last_updated:  '2026-10-10T08:00:00Z',
        ...more
    };
}

/** Another station: one DC point, busy. */
export function lobeda(more: Partial<ChargingLocation> = {}): ChargingLocation {
    return {
        id:            'DEMO-JENA-LOBEDA',
        name:          'Lobeda Stadtteilzentrum',
        address:       'Karl-Marx-Allee 20',
        postal_code:   '07747',
        city:          'Jena',
        country:       'DEU',
        coordinates:   { latitude: '50.894480', longitude: '11.606210' },
        operator:      { name: 'Thüringen HPC (demo)' },
        evses:         [
            { uid: '1', evse_id: 'DE*THP*E3001*1', status: 'CHARGING',
              connectors: [ { id: '1', standard: 'IEC_62196_T2_COMBO', format: 'CABLE', power_type: 'DC', max_electric_power: 300000 } ] }
        ],
        tariff:        { price_per_kwh: 0.79, currency: 'EUR' },
        last_updated:  '2026-10-10T08:00:00Z',
        ...more
    };
}

/** The map with both stations. */
export function mapData(locations: ChargingLocation[] = [ holzmarkt(), lobeda() ]): MapData {
    return {
        source:       'demo',
        stationsURL:  '',
        tiles:        'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        timestamp:    '2026-10-10T08:00:00Z',
        statuses:     [ 'AVAILABLE', 'CHARGING', 'RESERVED', 'BLOCKED', 'INOPERATIVE', 'OUTOFORDER', 'PLANNED', 'REMOVED', 'UNKNOWN' ],
        locations
    };
}

/** A session charging at the Holzmarkt. */
export function session(more: Partial<ChargingSession> = {}): ChargingSession {
    return {
        id:               'A1B2C3D4E5F6',
        status:           'charging',
        simulated:        true,
        location:         { id: 'DEMO-JENA-HOLZMARKT', name: 'Jena Holzmarkt', address: 'Holzmarkt 1', city: 'Jena', operator: 'Saaletal Strom (demo)' },
        evse:             { uid: '1', evseId: 'DE*SST*E2001*1', standard: 'IEC_62196_T2', powerType: 'AC_3_PHASE', maxPowerKW: 22 },
        startedAt:        '2026-10-10T08:00:00Z',
        startedBy:        'root',
        endedAt:          null,
        fullAt:           '2026-10-10T10:43:38Z',
        durationSeconds:  600,
        powerKW:          11,
        energyKWh:        1.833,
        targetEnergyKWh:  30,
        progressPercent:  6.1,
        cost:             { amount: 0.9, pricePerKWh: 0.49, currency: 'EUR' },
        ...more
    };
}

/**
 * The session at the Holzmarkt as the e-mobility provider reports it: started
 * through it, the charge point operator's figures - and no power, no target
 * and no time it is full at, which OCPI does not tell.
 */
export function reportedSession(more: Partial<ChargingSession> = {}): ChargingSession {

    const reported: ChargingSession = {
        ...session(),
        simulated:      false,
        chargingSince:  '2026-10-10T08:00:07Z',
        energyKWh:      4.2,
        cost:           { amount: 2.73, pricePerKWh: 0.49, currency: 'EUR', reported: true },
        ticket:         { id: '9f2c41d08a7e3b65c0ffee0123456789', party: 'DE*GDF',
                          notBefore: '2026-10-10T08:00:00Z', notAfter: '2026-10-10T09:00:00Z',
                          keyThumbprint: '6cab95a41a13b2c42f552b7c81248078b3f319afe7d3caf133bc4b4dbead1605',
                          issuerFingerprint: '0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0' },
        start:          { token: 'T-9F2C41D08A7E3B65', authorizationReference: '8e1c0ffee0123456789abcdef0123456',
                          requestDigest: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
                          countersignedAt: '2026-10-10T08:00:02Z' },
        provider:       { state: 'started', receivedAt: '2026-10-10T08:10:00Z', cdr: false, result: 'ACCEPTED', sessionStatus: 'ACTIVE' }
    };

    delete reported.fullAt;
    delete reported.powerKW;
    delete reported.targetEnergyKWh;
    delete reported.progressPercent;

    return { ...reported, ...more };

}


/** A charging pool of a roaming network, as the map lists it: its two EVSEs of a status not known, and no price yet. */
export function paradies(more: Partial<ChargingLocation> = {}): ChargingLocation {
    return {
        id:            'DE*GEF*P1',
        name:          'Jena, Paradiesbahnhof',
        address:       'Am Paradiesbahnhof 1',
        postal_code:   '07743',
        city:          'Jena',
        country:       'DEU',
        coordinates:   { latitude: '50.922100', longitude: '11.585200' },
        operator:      { name: 'GraphDefined Charging' },
        evses:         [
            { uid: 'DE*GEF*E1*1', evse_id: 'DE*GEF*E1*1', station_id: 'DE*GEF*S1', status: 'UNKNOWN',
              connectors: [ { id: '1', standard: 'IEC_62196_T2', format: 'SOCKET', power_type: 'AC_3_PHASE', max_electric_power: 22000 } ] },
            { uid: 'DE*GEF*E2*1', evse_id: 'DE*GEF*E2*1', station_id: 'DE*GEF*S2', status: 'UNKNOWN',
              connectors: [ { id: '1', standard: 'IEC_62196_T2_COMBO', format: 'CABLE', power_type: 'DC', max_electric_power: 150000 } ] }
        ],
        tariff:        null,
        last_updated:  '2026-10-10T08:00:00Z',
        verified:      null,
        ...more
    };
}

/** The map of the roaming network QA. */
export function roamingMapData(locations: ChargingLocation[] = [ paradies() ]): MapData {
    return {
        ...mapData(locations),
        source:          'roamingNetwork',
        roamingNetwork:  { id: 'QA', name: 'Quality Assurance', from: 'https://hub.example/poi/RNs/QA', error: null }
    };
}

/** The check of a station of the pool: its proof attested by OCC, or - not verified - why not. */
export function stationCheck(stationId: string, evseId: string, verified = true, more: Partial<StationCheck> = {}): StationCheck {

    if (!verified)
        return {
            stationId, checkedAt: '2026-10-10T08:00:00Z', from: 'https://hub.example/poi/RNs/QA', verified: false,
            error: `The proof of ${stationId} is not attested by the operator the trust anchor names: no attestation of a trusted key.`,
            ...more
        };

    return {
        stationId,
        checkedAt:                '2026-10-10T08:00:00Z',
        from:                     'https://hub.example/poi/RNs/QA',
        verified:                 true,
        error:                    null,
        textMismatch:             null,
        roamingNetwork:           { id: 'QA', name: 'Quality Assurance', description: null, operator: 'Open Charging Cloud', revision: 7, commitId: 'c0ffee' },
        attestation:              { role: 'roamingNetworkOperator', signer: 'OCC', algorithm: 'ES256', keyId: 'a1b2', signedAt: '2026-10-10T07:55:00Z', validUntil: '2026-10-10T08:10:00Z' },
        chargingStationOperator:  { id: 'DE*GEF', name: 'GraphDefined Charging' },
        station:                  { name: null, manufacturer: 'GraphDefined', model: 'GD-DC300', hardwareRevision: null, firmwareVersion: null },
        evses:                    [ {
            id:            evseId,
            maxPowerKW:    150,
            currentTypes:  [ 'DC' ],
            connectors:    [ { id: '1', type: 'IEC_62196_T2_COMBO', format: 'CABLE' } ],
            tariffs:       [ { id: 'DE*GEF*T_DC', name: 'DC', currency: 'EUR', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.69, stepSize: 1 } ] } ] } ],
            price:         { pricePerKWh: 0.65, currency: 'EUR' },
            energyMeter:   { id: 'meter-dc-1', manufacturer: 'Kernberg', model: 'KDM-500', serialNumber: 'DC-2026-001', firmwareVersion: null,
                             signatureMethods: [ 'OCMF' ],
                             publicKeys: [ { id: 'fba865f35d75', algorithm: 'secp256r1', key: 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0J5uv5Eo22C9', certificateChain: 0 } ] }
        } ],
        priceStatements:          [ { verified: true, error: null, entityType: 'EVSE', entityId: evseId, signer: 'DE*GEF', keyId: '4197', sequence: 0,
                                      signedAt: '2026-10-10T07:59:00Z', validFrom: '2026-10-10T07:59:00Z', validUntil: null,
                                      prices: [ { chargingTariffId: 'DE*GEF*T_DC', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.65, stepSize: 1 } ] } ] } ] } ],
        priceStatementsError:     null,
        ...more
    };

}

/** The check of the pool: both of its stations, holding unless said otherwise. */
export function locationCheck(verified = true): LocationCheck {
    const stations = [ stationCheck('DE*GEF*S1', 'DE*GEF*E1*1', verified), stationCheck('DE*GEF*S2', 'DE*GEF*E2*1', verified) ];
    return { locationId: 'DE*GEF*P1', name: 'Jena, Paradiesbahnhof', verified, checkedAt: '2026-10-10T08:00:00Z', stations };
}
