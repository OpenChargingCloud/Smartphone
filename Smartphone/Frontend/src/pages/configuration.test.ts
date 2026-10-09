/**
 * The configuration drawn, in a document of happy-dom, against a stand-in
 * smartphone: its cards stand as the markup they are, the frame's Reload asks
 * the smartphone again and draws them with what it says then, and a
 * configuration that cannot be had is said so.
 */

import { asked, open, refused, until, type Asked } from '../../test/smartphone.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { configurationPage } = await import('./configuration.ts');


let uptime     = '1 minute';
let available  = true;

function smartphone({ path }: Asked): unknown {

    if (path === '/status')
        return { service: 'Smartphone', version: '1.0', hermod: null, timestamp: '2026-10-04T12:00:00Z',
                 startedAt: '2026-10-04T11:59:00Z', uptime, sessions: 1, log: { entries: 0, capacity: 100, lastId: 0, tags: [] } };

    if (path === '/configuration')
        return available
                   ? { smartphone: { machine: 'phone001' }, http: { port: 3000 }, web: {}, log: {}, time: {},
                       assemblies: [ { name: 'Smartphone', version: '1.0', commit: 'abc1234' } ] }
                   : refused(500, 'the configuration file could not be read');

    return undefined;

}


describe('the configuration', () => {

    it('draws its cards as markup, and again on Reload', async () => {

        uptime     = '1 minute';
        available  = true;

        const root = await open(configurationPage, '/configuration', [ 'configuration:read' ],
                                smartphone, root => root.querySelector('.cards') !== null);

        assert.equal(root.querySelectorAll('.cards > section.card').length, 6);
        assert.match(root.querySelector('.cards')!.textContent!, /phone001/);
        assert.match(root.querySelector('.cards')!.textContent!, /Uptime\s+1 minute/);
        assert.doesNotMatch(root.textContent!, /<section|<div/, 'a card was taken as text');

        const configurationsAsked = () => asked.filter(one => one.method === 'GET' && one.path === '/configuration').length;
        assert.equal(configurationsAsked(), 1);

        uptime = '2 minutes';
        root.querySelector<HTMLButtonElement>('.page-actions #reload')!.click();

        await until(() => /Uptime\s+2 minutes/.test(root.textContent!), 'Reload did not draw what the smartphone says then');

        assert.equal(configurationsAsked(), 2, 'Reload did not ask the smartphone again');
        assert.equal(root.querySelectorAll('.cards > section.card').length, 6);

    });

    it('says so where the configuration cannot be had, and draws it once Reload has it', async () => {

        available = false;

        const root = await open(configurationPage, '/configuration', [ 'configuration:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /The configuration could not be loaded: .*could not be read/);
        assert.equal(root.querySelector('.cards'), null);

        available = true;
        root.querySelector<HTMLButtonElement>('.page-actions #reload')!.click();

        await until(() => root.querySelector('.cards') !== null, 'Reload did not draw the configuration');

        assert.equal(root.querySelector('.error-box'), null, 'the error stayed beside the cards');

    });

});
