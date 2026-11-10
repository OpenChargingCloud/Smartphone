/*
 * What the tests of the map and the charging pages are given to draw: two
 * stations as a smartphone sends them, and a charging session as it sends one;
 * a charging pool of a roaming network, with the check of its stations; and
 * the check of a session there. Each test lays over them what it is about.
 */

import type { ChargingLocation, ChargingSession, LocationCheck, MapData, MeterValueCheck, RoamingNetworkStatus, SessionCheck, StationCheck } from '../src/api/client';


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


/**
 * A session at the DC EVSE of a roaming network that the web payment page of
 * its operator started, as a smartphone sends it: followed over its live link,
 * two signed documents come, its figures what they say, its signed start value
 * and the value after it the meter's as the proof of the station says.
 */
export function liveLinkSession(more: Partial<ChargingSession> = {}): ChargingSession {

    const followed: ChargingSession = {
        ...session(),
        id:             'L1V3L1NK0001',
        simulated:      false,
        location:       { id: 'DE*GEF*P1', name: 'Jena, Paradiesbahnhof', address: 'Am Paradiesbahnhof 1', city: 'Jena', operator: 'GraphDefined' },
        evse:           { uid: 'DE*GEF*E2*1', evseId: 'DE*GEF*E2*1', standard: 'IEC_62196_T2_COMBO', powerType: 'DC', maxPowerKW: 150 },
        chargingSince:  '2026-10-10T08:00:00Z',
        energyKWh:      12.5,
        cost:           { amount: 8.13, pricePerKWh: 0.65, currency: 'EUR' },
        liveLink:       { handedOverAt: '2026-10-10T07:59:30Z', from: 'https://pay.gef.example/liveLinks/S1', evseId: 'DE*GEF*E2*1',
                          transports: [ 'https' ], updates: 7, lastUpdateAt: '2026-10-10T08:10:00Z', lastTransport: 'https',
                          lastEndpoint: 'https://pay.gef.example/sessions/S1/live', lastKind: 'OCMFChargeTransparencyRecord',
                          problem: null, endedAt: null, endedBecause: null, documents: 2,
                          readings: [ { measuredAt: '2026-10-10T08:00:00Z', energyKWh: 1000,   transaction: 'B', status: 'G', holds: true },
                                      { measuredAt: '2026-10-10T08:10:00Z', energyKWh: 1012.5, transaction: 'C', status: 'G', holds: true } ],
                          energyKWh: 12.5,
                          start: { checkedAt: '2026-10-10T08:00:05Z', holds: true, error: null,
                                   reading: { measuredAt: '2026-10-10T08:00:00Z', energyKWh: 1000, transaction: 'B', status: 'G' },
                                   meterSerialNumber: 'DC-2026-001', meterId: 'meter-dc-1',
                                   keyId: '95ceb48cc35ae3335c7f06fa0a8b8aff446da032057708eec66a7edc371591f6', result: 'ValidSignature',
                                   identification: 'web-payment-S1' },
                          values: { holding: 1, wrong: 0, undecided: 0, notChecked: 0 },
                          wrong: null, refused: false, end: null, stopLink: null, limit: null, stop: null, limits: [], ending: null,
                          disputeBecause: null, disputeLink: null, dispute: null, paymentReference: 'web-payment-S1',
                          webPaymentHost: 'pay.gef.example', handOver: 'pay.gef.example',
                          bound: { stopLink: false, disputeLink: false, paymentReference: true, paymentLink: false },
                          paymentLink: null, payment: null }
    };

    delete followed.fullAt;
    delete followed.powerKW;
    delete followed.targetEnergyKWh;
    delete followed.progressPercent;

    return { ...followed, ...more };

}


/**
 * The session of a live link the web payment page gave a stop link for, and
 * the limit of 20 kWh the driver chose there, as a smartphone sends it.
 */
export function stopLinkSession(more: Partial<ChargingSession> = {}): ChargingSession {

    const followed = liveLinkSession();

    return {
        ...followed,
        liveLink: { ...followed.liveLink!,
                    stopLink: 'pay.gef.example',
                    limit:    { maxEnergy: 20000 },
                    limits:   [ { kind: 'energy', limit: 20, unit: 'kWh', value: 12.5, reached: false } ] },
        ...more
    };

}


/**
 * The session of a live link that ended at the limit of 20 kWh the driver
 * chose at the web payment page: the meter signed its end, as a smartphone
 * sends it.
 */
export function endedAtLimitSession(): ChargingSession {

    const followed = stopLinkSession();
    const link     = followed.liveLink!;

    return {
        ...followed,
        status:     'finished',
        endedAt:    '2026-10-10T08:16:00Z',
        energyKWh:  20,
        cost:       { amount: 13, pricePerKWh: 0.65, currency: 'EUR' },
        liveLink:   { ...link, documents: 3, endedAt: '2026-10-10T08:16:00Z',
                      endedBecause: 'The meter signed the end of the transaction ("E") at 2026-10-10T08:16:00.0000000+00:00.',
                      readings: [ ...link.readings, { measuredAt: '2026-10-10T08:16:00Z', energyKWh: 1020, transaction: 'E', status: 'G', holds: true } ],
                      energyKWh: 20,
                      values: { ...link.values, holding: 2 },
                      end: { checkedAt: '2026-10-10T08:16:05Z', holds: true, error: null,
                             meterSerialNumber: 'DC-2026-001', meterId: 'meter-dc-1',
                             keyId: '95ceb48cc35ae3335c7f06fa0a8b8aff446da032057708eec66a7edc371591f6', result: 'ValidSignature',
                             identification: 'web-payment-S1',
                             readings: [ { measuredAt: '2026-10-10T08:16:00Z', energyKWh: 1020, transaction: 'E', status: 'G' } ] },
                      limits: [ { kind: 'energy', limit: 20, unit: 'kWh', value: 20, reached: true } ],
                      ending: { reason: 'limit', transaction: 'E',
                                because: 'It ended at its limit of 20 kWh: 20 kWh, as its signed values say - its meter signed the end of the transaction ("E").' } }
    };

}


/**
 * The session of a live link whose signed start value is a wrong one - not
 * signed by the key the proof gave for the meter -, which the smartphone
 * stopped, as it sends it.
 */
export function wrongStartSession(): ChargingSession {

    const followed = liveLinkSession();
    const link     = followed.liveLink!;

    return {
        ...followed,
        status:          'stopped',
        endedAt:         '2026-10-10T08:00:05Z',
        energyKWh:       0,
        cost:            { amount: 0, pricePerKWh: 0.65, currency: 'EUR' },
        stoppedBecause:  'Its signed start meter value is a wrong one: It is not signed by the key 95ceb48c of the meter DC-2026-001: InvalidSignature.',
        liveLink:        { ...link, updates: 1, documents: 1, readings: [ { ...link.readings[0]!, holds: false } ], energyKWh: 0, refused: true,
                           disputeBecause: 'Its signed start meter value is a wrong one: It is not signed by the key 95ceb48c of the meter DC-2026-001: InvalidSignature.',
                           start: { ...link.start!, holds: false, result: 'InvalidSignature',
                                    error: 'It is not signed by the key 95ceb48c of the meter DC-2026-001: InvalidSignature.' },
                           values: { holding: 0, wrong: 0, undecided: 0, notChecked: 0 } }
    };

}


/**
 * The session of a live link whose signed start value held and whose value
 * after it is a wrong one - the meter counting backwards -, which the
 * smartphone stopped, as it sends it.
 */
export function wrongValueSession(): ChargingSession {

    const followed = liveLinkSession();
    const link     = followed.liveLink!;
    const error    = 'The meter counts backwards: 990 kWh at 2026-10-10T08:10:00.0000000+00:00, and 1000 kWh at 2026-10-10T08:00:00.0000000+00:00.';

    return {
        ...followed,
        status:          'stopped',
        endedAt:         '2026-10-10T08:10:05Z',
        stoppedBecause:  `A signed meter value after its start is a wrong one: ${error}`,
        liveLink:        { ...link, refused: true, disputeBecause: `A signed meter value after its start is a wrong one: ${error}`,
                           readings: [ link.readings[0]!, { ...link.readings[1]!, energyKWh: 990, holds: false } ],
                           values:   { holding: 0, wrong: 1, undecided: 0, notChecked: 0 },
                           wrong:    { checkedAt: '2026-10-10T08:10:05Z', holds: false, error,
                                       meterSerialNumber: 'DC-2026-001', meterId: 'meter-dc-1',
                                       keyId: '95ceb48cc35ae3335c7f06fa0a8b8aff446da032057708eec66a7edc371591f6', result: 'ValidSignature',
                                       identification: 'web-payment-S1',
                                       readings: [ { measuredAt: '2026-10-10T08:10:00Z', energyKWh: 990, transaction: 'C', status: 'G' } ] } }
    };

}


/**
 * The session of a live link whose signed end value is a wrong one - not
 * signed by the key the proof gave for the meter -: finished, and disputed
 * through the dispute link of the web payment page, which did not take the
 * dispute, as a smartphone sends it.
 */
export function wrongEndSession(): ChargingSession {

    const followed = liveLinkSession();
    const link     = followed.liveLink!;
    const error    = 'It is not signed by the key 95ceb48c of the meter DC-2026-001: InvalidSignature.';

    return {
        ...followed,
        status:     'finished',
        endedAt:    '2026-10-10T08:16:00Z',
        liveLink:   { ...link, documents: 3, endedAt: '2026-10-10T08:16:00Z',
                      endedBecause: 'The meter signed the end of the transaction ("E") at 2026-10-10T08:16:00.0000000+00:00.',
                      readings: [ ...link.readings, { measuredAt: '2026-10-10T08:16:00Z', energyKWh: 1020, transaction: 'E', status: 'G', holds: false } ],
                      values: { ...link.values, wrong: 1 },
                      end: { checkedAt: '2026-10-10T08:16:05Z', holds: false, error,
                             meterSerialNumber: 'DC-2026-001', meterId: null, keyId: null, result: 'InvalidSignature',
                             identification: 'web-payment-S1',
                             readings: [ { measuredAt: '2026-10-10T08:16:00Z', energyKWh: 1020, transaction: 'E', status: 'G' } ] },
                      ending: { reason: 'wrong', transaction: null,
                                because: `Its signed end meter value is a wrong one: ${error} How it ended, and against its limit, this phone takes nothing of it.` },
                      disputeBecause: `Its signed end meter value is a wrong one: ${error}`,
                      disputeLink:    'pay.gef.example',
                      dispute:        { requestedAt: '2026-10-10T08:16:05Z', requestedBy: 'this phone', reason: `Its signed end meter value is a wrong one: ${error}`,
                                        accepted: false, answer: '409 Conflict: Nothing is disputed here.' } }
    };

}


/**
 * The check of a session at the DC EVSE of a roaming network whose charge
 * detail record came: its begin and its end signed by the meter, with the key
 * the station's proof gave, and the price the operator stated for the EVSE in
 * force at the start - as a smartphone sends it.
 */
export function sessionCheck(more: Partial<SessionCheck> = {}): SessionCheck {

    const value = (nature: string, transaction: string, at: string, kWh: number): MeterValueCheck => ({
        nature,
        verified:           true,
        error:              null,
        result:             'ValidSignature',
        meterSerialNumber:  'DC-2026-001',
        meterId:            'meter-dc-1',
        keyId:              '95ceb48cc35ae3335c7f06fa0a8b8aff446da032057708eec66a7edc371591f6',
        identification:     'T-9F2C41D08A7E3B65',
        ofThisStart:        true,
        readings:           [ { measuredAt: at, energyKWh: kWh, transaction, status: 'G' } ]
    });

    return {
        checkedAt:  '2026-10-10T09:31:00Z',
        error:      null,
        price:      {
            error:                   null,
            signatures:              'valid',
            applicability:           'unbound',
            agreement:               'missing',
            publication:             'missing',
            publicationBeforeStart:  'not-proven',
            chainGaps:               false,
            customerConsent:         'not-proven',
            diagnostics:             [],
            selected:                { entityType: 'EVSE', entityId: 'DE*GEF*E2*1', signer: 'DE*GEF', sequence: 0,
                                       signedAt: '2026-10-10T07:59:59Z', digest: 'c0ffee0123456789c0ffee0123456789c0ffee0123456789c0ffee0123456789',
                                       tariffId: 'DE*GEF*T_DC', currency: 'EUR', pricePerKWh: 0.65 },
            matchesSession:          true
        },
        meterValues: {
            verified:       true,
            error:          null,
            values:         [ value('Start', 'B', '2026-10-10T08:00:07Z', 1000), value('End', 'E', '2026-10-10T09:30:00Z', 1012.5) ],
            startKWh:       1000,
            endKWh:         1012.5,
            energyKWh:      12.5,
            cdrEnergyKWh:   12.5,
            energyMatches:  true,
            expectedCost:   8.13,
            cdrCost:        8.13,
            costMatches:    true,
            timeBasis:      'signed-measurement-timestamps'
        },
        ...more
    };

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

/** The map of the roaming network QA - the status of its charge points followed, or not. */
export function roamingMapData(locations: ChargingLocation[] = [ paradies() ], status: RoamingNetworkStatus | null = null): MapData {
    return {
        ...mapData(locations),
        source:          'roamingNetwork',
        roamingNetwork:  { id: 'QA', name: 'Quality Assurance', from: 'https://hub.example/poi/RNs/QA', error: null, status }
    };
}

/** The status of the charge points of QA, followed over its WebSocket since 08:00 - or, not live, why not. */
export function roamingStatus(live = true, error: string | null = null): RoamingNetworkStatus {
    return live
               ? { live: true,  from: 'wss://hub.example/poi/RNs/QA/history/ws', since: '2026-10-10T08:00:00Z', error: null, evses: 2 }
               : { live: false, from: null, since: null, error, evses: 0 };
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
        trust:                    { signaturesValid: true, signersTrusted: true, quorumReached: true, withinValidity: true, trustedHolders: 1, requiredConsensus: 1,
                                    attestedBy: [ 'OCC' ], validUntil: '2026-10-10T08:10:00Z' },
        textMismatch:             null,
        warnings:                 [],
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
        priceStatements:          [ { verified: true, current: true, error: null, entityType: 'EVSE', entityId: evseId, signer: 'DE*GEF', keyId: '4197', sequence: 0,
                                      signedAt: '2026-10-10T07:59:00Z', validFrom: '2026-10-10T07:59:00Z', validUntil: null,
                                      prices: [ { chargingTariffId: 'DE*GEF*T_DC', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.65, stepSize: 1 } ] } ] } ] } ],
        priceStatementsError:     null,
        priceConflict:            null,
        ...more
    };

}

/** The check of the pool: both of its stations, holding unless said otherwise. */
export function locationCheck(verified = true): LocationCheck {
    const stations = [ stationCheck('DE*GEF*S1', 'DE*GEF*E1*1', verified), stationCheck('DE*GEF*S2', 'DE*GEF*E2*1', verified) ];
    return { locationId: 'DE*GEF*P1', name: 'Jena, Paradiesbahnhof', verified, checkedAt: '2026-10-10T08:00:00Z', stations };
}
