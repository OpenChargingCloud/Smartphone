import type { ChargingLocation, EVSE, EVSEStatus, LocationCheck, WebPayment } from '../api/client';

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


/**
 * The web payment page to offer at a pool of the roaming network, where a
 * driver pays ad hoc and starts: that of the station of the EVSE chosen,
 * where its proof holds - else of the first station whose proof holds and
 * names one. Only an https URL is linked; the phone has checked that, and it
 * is checked here again, as a link is what a page goes to.
 */
export function webPaymentOf(Check: LocationCheck | undefined, EVSEId?: string): WebPayment | null {

    const verified  = (Check?.stations ?? []).filter(station => station.verified);
    const ofEVSE    = EVSEId !== undefined ? verified.find(station => (station.evses ?? []).some(evse => evse.id === EVSEId)) : undefined;
    const payment   = ofEVSE !== undefined
                          ? ofEVSE.webPayment ?? null
                          : verified.map(station => station.webPayment).find(found => found !== undefined && found !== null) ?? null;

    return offeredWebPayment(payment);

}


/** A web payment page as it may be linked: an https URL only, another one said in its error. */
export function offeredWebPayment(Payment: WebPayment | null | undefined): WebPayment | null {

    return Payment !== undefined && Payment !== null && Payment.url !== null && !Payment.url.startsWith('https://')
               ? { ...Payment, url: null, error: Payment.error ?? `'${Payment.url}' is no https URL, so it is not offered.` }
               : Payment ?? null;

}


/** Whose web payment page it is: the proof gives the station's own, else its pool's, else its operator's. */
export function webPaymentWord(Of: WebPayment['of']): string {

    switch (Of) {
        case 'chargingStation':  return 'of its station';
        case 'chargingPool':     return 'of its pool';
        default:                 return 'of its operator';
    }

}
