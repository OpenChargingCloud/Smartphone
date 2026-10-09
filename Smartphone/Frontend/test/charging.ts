/*
 * What the tests of the map and the charging pages are given to draw: two
 * stations as a smartphone sends them, and a charging session as it sends one.
 * Each test lays over them what it is about.
 */

import type { ChargingLocation, ChargingSession, MapData } from '../src/api/client';


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
