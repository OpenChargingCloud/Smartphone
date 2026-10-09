/**
 * What the map says about charging stations apart from drawing them: where
 * one is, what colour its pin has, and which of them a search finds.
 */

import { holzmarkt, lobeda } from '../../test/charging.ts';
import { availability, maxPowerKW, positionOf, search, standardWord, stationStatus } from './stations.ts';

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
