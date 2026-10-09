import type { ChargingLocation, EVSE, EVSEStatus } from '../api/client';

/**
 * What the map says about charging stations, apart from drawing them: where
 * one is, what it is as a whole, what its connectors are called, and which
 * of them a search finds. Kept apart from the page so that it can be asked
 * without a map.
 */


/** Where a station is: OCPI says it as text, a map wants numbers. */
export function positionOf(Location: ChargingLocation): [ number, number ] | null {

    const latitude   = Number.parseFloat(Location.coordinates.latitude);
    const longitude  = Number.parseFloat(Location.coordinates.longitude);

    return Number.isFinite(latitude)  && Math.abs(latitude)  <=  90 &&
           Number.isFinite(longitude) && Math.abs(longitude) <= 180
               ? [ latitude, longitude ]
               : null;

}


/**
 * What a station is as a whole, for the colour of its pin: available where
 * any of its EVSEs is - that is what a driver looks for - and otherwise the
 * best of what the others are, charging before reserved before out of order.
 */
export function stationStatus(Location: ChargingLocation): EVSEStatus {

    const order: EVSEStatus[] = [ 'AVAILABLE', 'CHARGING', 'RESERVED', 'BLOCKED', 'INOPERATIVE', 'OUTOFORDER', 'PLANNED', 'REMOVED', 'UNKNOWN' ];

    for (const status of order)
        if (Location.evses.some(evse => evse.status === status))
            return status;

    return 'UNKNOWN';

}


/** How many of a station's EVSEs are available, of how many. */
export function availability(Location: ChargingLocation): { available: number; total: number } {
    return {
        available:  Location.evses.filter(evse => evse.status === 'AVAILABLE').length,
        total:      Location.evses.length
    };
}


/** A status as a person reads it. */
export function statusWord(Status: EVSEStatus): string {
    switch (Status) {
        case 'AVAILABLE':    return 'available';
        case 'CHARGING':     return 'charging';
        case 'RESERVED':     return 'reserved';
        case 'BLOCKED':      return 'blocked';
        case 'INOPERATIVE':  return 'inoperative';
        case 'OUTOFORDER':   return 'out of order';
        case 'PLANNED':      return 'planned';
        case 'REMOVED':      return 'removed';
        default:             return 'unknown';
    }
}


/** The class a status is coloured by: "status-outoforder". */
export function statusClass(Status: string): string {
    return `status-${Status.toLowerCase()}`;
}


/** A connector standard as a driver calls it. */
export function standardWord(Standard: string): string {
    switch (Standard) {
        case 'IEC_62196_T2':        return 'Type 2';
        case 'IEC_62196_T2_COMBO':  return 'CCS';
        case 'CHADEMO':             return 'CHAdeMO';
        case 'IEC_62196_T1':        return 'Type 1';
        case 'IEC_62196_T1_COMBO':  return 'CCS 1';
        case 'TESLA_S':             return 'Tesla';
        case 'DOMESTIC_F':          return 'Schuko';
        default:                    return Standard;
    }
}


/** What an EVSE gives at most, in kW: the most of its connectors. */
export function maxPowerKW(EVSE: EVSE): number {
    return Math.max(0, ...EVSE.connectors.map(connector => connector.max_electric_power / 1000));
}


/**
 * The stations a search finds: those whose name, address, postal code, city,
 * operator or one of whose EVSE IDs holds every word typed, whatever its case
 * - the ones whose name holds them first. Nothing typed finds nothing: the map
 * shows every station anyway.
 */
export function search(Locations: readonly ChargingLocation[],
                       Text:      string): ChargingLocation[] {

    const words = Text.toLocaleLowerCase().split(/\s+/).filter(word => word.length > 0);

    if (words.length === 0)
        return [];

    const textOf = (location: ChargingLocation) => [
                       location.name, location.address, location.postal_code, location.city,
                       location.operator.name, ...location.evses.map(evse => evse.evse_id)
                   ].join(' ').toLocaleLowerCase();

    const found  = Locations.filter(location => words.every(word => textOf(location).includes(word)));
    const inName = (location: ChargingLocation) => words.every(word => location.name.toLocaleLowerCase().includes(word)) ? 0 : 1;

    return found.sort((a, b) => inName(a) - inName(b) || a.name.localeCompare(b.name));

}
