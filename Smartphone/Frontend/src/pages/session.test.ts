/**
 * One charging session, drawn in a document of happy-dom against a stand-in
 * smartphone: what it charged and where, Stop while it charges and not after,
 * and a session that is not there said so.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { session } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { sessionPage } = await import('./session.ts');

after(() => leave());


let current = session();

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === `/charging/sessions/${current.id}`)
        return current;

    if (method === 'POST' && path === `/charging/sessions/${current.id}/stop`) {
        current = { ...current, status: 'stopped', endedAt: '2026-10-10T08:10:00Z', powerKW: 0, stoppedBy: 'root' };
        return current;
    }

    if (method === 'GET' && path.startsWith('/charging/sessions/'))
        return refused(404, `There is no charging session '${path.split('/').pop()}' on this phone.`);

    return undefined;

}


describe('a charging session', () => {

    it('says where, what and how much, and stops on Stop', async () => {

        current = session();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.btn.danger') !== null);

        assert.match(root.textContent!, /Jena Holzmarkt/);
        assert.match(root.textContent!, /DE\*SST\*E2001\*1/);
        assert.match(root.textContent!, /Type 2/);
        assert.match(root.textContent!, /Started by\s*root/);
        assert.ok(root.querySelector('progress.session-progress'));

        root.querySelector<HTMLButtonElement>('.btn.danger')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === `/charging/sessions/${current.id}/stop`),
                    'Stop did not ask the smartphone');
        await until(() => root.querySelector('.btn.danger') === null, 'Stop stayed after the session was over');

        assert.match(root.querySelector('.session-status')!.textContent!, /stopped/);
        assert.match(root.textContent!, /Stopped by\s*root/);

    });

    it('says so where there is no such session', async () => {

        const root = await open(sessionPage, '/charging/sessions/NOSUCHONE', [ 'charging:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging session 'NOSUCHONE'/);

    });

});
