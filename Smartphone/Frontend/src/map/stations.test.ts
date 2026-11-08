/**
 * What the map says about charging stations apart from drawing them: where
 * one is, what colour its pin has, which of them a search finds, and which
 * web payment page a pool of the roaming network offers.
 */

import { holzmarkt, lobeda, locationCheck } from '../../test/charging.ts';
import { availability, maxPowerKW, offeredWebPayment, positionOf, search, standardWord, stationStatus, webPaymentOf, webPaymentWord } from './stations.ts';
import type { LocationCheck, WebPayment } from '../api/client.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';


describe('a station of the map', () => {

    it('is where its coordinates say, read from the text OCPI has them as', () => {
        assert.deepEqual(positionOf(holzmarkt()), [ 50.92842, 11.58761 ]);
    });

    it('is nowhere where its coordinates are no coordinates, rather than at 0, 0', () => {
        assert.equal(positionOf(holzmarkt({ coordinates: { latitude: 'north', longitude: '11.5' } })), null);
        assert.equal(positionOf(holzmarkt({ coordinates: { latitude: '91',    longitude: '11.5' } })), null);
    });

    it('is available where any of its points is, whatever the others are', () => {
        assert.equal(stationStatus(holzmarkt()), 'AVAILABLE');
        assert.deepEqual(availability(holzmarkt()), { available: 1, total: 2 });
    });

    it('is charging where none is available and one is charging, and out of order before it is unknown', () => {

        assert.equal(stationStatus(lobeda()), 'CHARGING');

        const broken = holzmarkt();
        broken.evses = broken.evses.map(evse => ({ ...evse, status: 'OUTOFORDER' as const }));

        assert.equal(stationStatus(broken), 'OUTOFORDER');
        assert.equal(stationStatus(holzmarkt({ evses: [] })), 'UNKNOWN');

    });

    it('says its connectors and its power as a driver does', () => {
        assert.equal(standardWord('IEC_62196_T2_COMBO'), 'CCS');
        assert.equal(maxPowerKW(lobeda().evses[0]!), 300);
    });

});


describe('the search of the map', () => {

    const both = [ lobeda(), holzmarkt() ];

    it('finds nothing where nothing is typed, as the map shows every station anyway', () => {
        assert.deepEqual(search(both, '   '), []);
    });

    it('finds a station by its name, its street, its city, its operator or an EVSE ID, whatever the case', () => {
        assert.deepEqual(search(both, 'holzmarkt').map(one => one.id),      [ 'DEMO-JENA-HOLZMARKT' ]);
        assert.deepEqual(search(both, 'MARX').map(one => one.id),           [ 'DEMO-JENA-LOBEDA' ]);
        assert.deepEqual(search(both, 'thüringen').map(one => one.id),      [ 'DEMO-JENA-LOBEDA' ]);
        assert.deepEqual(search(both, 'de*sst*e2001').map(one => one.id),   [ 'DEMO-JENA-HOLZMARKT' ]);
    });

    it('finds only what holds every word typed, and the ones named so first', () => {
        assert.deepEqual(search(both, 'jena').map(one => one.id),           [ 'DEMO-JENA-HOLZMARKT', 'DEMO-JENA-LOBEDA' ]);
        assert.deepEqual(search(both, 'jena 07747').map(one => one.id),     [ 'DEMO-JENA-LOBEDA' ]);
        assert.deepEqual(search(both, 'lobeda jena').map(one => one.id),    [ 'DEMO-JENA-LOBEDA' ]);
    });

});


describe('the web payment page of a pool of the roaming network', () => {

    function withPayments(...payments: (WebPayment | null)[]): LocationCheck {
        const check = locationCheck();
        check.stations.forEach((station, index) => station.webPayment = payments[index] ?? null);
        return check;
    }

    const ofS1        : WebPayment = { url: 'https://pay.gef.example/stations/S1', of: 'chargingStation',         error: null };
    const ofOperator  : WebPayment = { url: 'https://pay.gef.example/',            of: 'chargingStationOperator', error: null };

    it('is that of the station of the charge point chosen', () => {
        assert.deepEqual(webPaymentOf(withPayments(ofS1, ofOperator), 'DE*GEF*E2*1'), ofOperator);
        assert.deepEqual(webPaymentOf(withPayments(ofS1, ofOperator), 'DE*GEF*E1*1'), ofS1);
    });

    it('is none where the station of the charge point chosen names none, rather than another station\'s', () => {
        assert.equal(webPaymentOf(withPayments(null, ofOperator), 'DE*GEF*E1*1'), null);
    });

    it('is that of the first station naming one, where no charge point is chosen', () => {
        assert.deepEqual(webPaymentOf(withPayments(null, ofOperator)), ofOperator);
    });

    it('is none where no proof holds', () => {
        const check = withPayments(ofS1, ofOperator);
        check.stations.forEach(station => station.verified = false);
        assert.equal(webPaymentOf(check, 'DE*GEF*E1*1'), null);
        assert.equal(webPaymentOf(undefined), null);
    });

    it('is linked only where it is https, and said otherwise', () => {
        const plain = offeredWebPayment({ url: 'http://pay.gef.example/', of: 'chargingPool', error: null });
        assert.equal(plain?.url, null);
        assert.match(plain!.error!, /'http:\/\/pay\.gef\.example\/' is no https URL/);
        assert.equal(offeredWebPayment({ url: 'javascript:alert(1)', of: 'chargingPool', error: null })?.url, null);
        assert.deepEqual(offeredWebPayment(ofS1), ofS1);
    });

    it('says whose it is', () => {
        assert.equal(webPaymentWord('chargingStation'),         'of its station');
        assert.equal(webPaymentWord('chargingPool'),            'of its pool');
        assert.equal(webPaymentWord('chargingStationOperator'), 'of its operator');
    });

});
