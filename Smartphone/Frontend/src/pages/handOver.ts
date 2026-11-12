import { api, type ChargingLocation } from '../api/client';
import { auth } from '../auth';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { mayButNot, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render } from '@node/view';

/** What the page is opened with: the charge point paid for, as the map named it, and the hand-over the web payment page added. */
export const handOverPath = '/charging/handOver';

/**
 * Where the web payment page of an operator sends the driver back to, once
 * the session started: the address of this page, with the charge point paid
 * for - the map names it as it opens the web payment page, as its
 * "handOverTo" - and nothing else. The web payment page adds "handOver", the
 * URL of its hand-over, and nothing of the driver.
 */
export function handOverTo(origin: string, locationId: string, evseUid: string): string {

    const to = new URL(toURL(handOverPath), origin);

    to.searchParams.set('locationId', locationId);
    to.searchParams.set('evseUid',    evseUid);

    return to.toString();

}

/**
 * The address of a web payment page with "handOverTo" added: where it sends
 * the driver back to. Whatever the page has in its query already stays.
 */
export function withHandOverTo(webPaymentURL: string, origin: string, locationId: string, evseUid: string): string {

    try
    {
        const page = new URL(webPaymentURL);
        page.searchParams.set('handOverTo', handOverTo(origin, locationId, evseUid));
        return page.toString();
    }
    catch
    {
        return webPaymentURL;
    }

}


/**
 * The session the web payment page of an operator started, handed over to the
 * phone without anything to copy: that page sent the driver here, with the
 * URL of its hand-over. Nothing is taken as the page opens - an address is
 * what anybody can make a link of -: it says which charge point and where the
 * hand-over is, and the driver takes it over. What is taken then is checked
 * as a hand-over pasted into the map is: from the host of the web payment
 * page the proof of the station names alone.
 */
export const handOverPage: Page = {

    title: 'Hand-over',

    render({ root, url, navigate }) {

        const content = shell(root, {
            active:    '/charging',
            title:     'Hand-over',
            subtitle:  'The session the web payment page of an operator started, handed over to this phone.'
        });

        const locationId  = url.searchParams.get('locationId')?.trim() ?? '';
        const evseUid     = url.searchParams.get('evseUid')?.trim()    ?? '';
        const handOver    = url.searchParams.get('handOver')?.trim()   ?? '';

        const mayCharge   = auth.can('charging', 'run');

        let cancelled  = false;
        let location:  ChargingLocation | null = null;
        let taking     = false;
        let failed:    string | null = null;

        // Only to say where it is: nothing of it is taken before the driver says so.
        const host = (() => {
            try
            {
                const at = new URL(handOver);
                return at.protocol === 'https:' ? at.host : null;
            }
            catch
            {
                return null;
            }
        })();

        const missing = locationId === '' || evseUid === ''
                            ? 'This address names no charge point: a hand-over is of the charge point paid for.'
                            : handOver === ''
                                  ? 'This address names no hand-over: the web payment page adds its URL as "handOver".'
                                  : host === null
                                        ? 'The hand-over this address names is no https URL.'
                                        : null;


        function draw(): void {

            const evse = location?.evses.find(one => one.uid === evseUid);

            render(content, html`

                <section class="card hand-over-page">

                    <h2><i class="fa-solid fa-handshake"></i> Take over a session</h2>

                    ${missing !== null ? html`
                        <div class="error-box">${missing}</div>
                        <p>Find the station on the <a href="${toURL('/map')}">map</a>, and paste what its page gave there.</p>
                    ` : html`

                        <div class="kv"><span class="k">Station</span><span class="v">${location !== null ? html`${location.name}, ${location.city}` : html`<span class="mono">${locationId}</span>`}</span></div>
                        <div class="kv"><span class="k">Charge point</span><span class="v mono">${evse?.evse_id ?? evseUid}</span></div>
                        <div class="kv"><span class="k">Hand-over</span><span class="v">at <span class="mono">${host}</span></span></div>

                        <p class="small muted">
                            It is taken only where that is the host of the web payment page the proof of the station names -
                            its live link, its links, the reference of the payment and the limit in one.
                        </p>

                        ${mayCharge ? html`
                            <button type="button" class="btn primary take-over" ?disabled=${taking} @click=${() => void take()}>
                                <i class="fa-solid fa-handshake"></i> ${taking ? 'Taking it over ...' : 'Take it over'}
                            </button>
                        ` : html`
                            <div class="notice">${mayButNot('look at the charging sessions', 'take one over')}</div>
                        `}

                        ${failed !== null ? html`<div class="form-error" role="alert">${failed}</div>` : nothing}

                    `}

                </section>

            `);

        }


        async function take(): Promise<void> {

            if (taking || missing !== null)
                return;

            taking  = true;
            failed  = null;
            draw();

            try
            {

                const session = await api.charging.takeOver(locationId, evseUid, handOver);

                // In place of this page: going back does not take it over again.
                if (!cancelled)
                    navigate(`/charging?session=${encodeURIComponent(session.id)}`, true);

            }
            catch (error)
            {
                failed = errorMessage(error);
            }
            finally
            {
                taking = false;
                if (!cancelled)
                    draw();
            }

        }


        draw();

        // The name of the station, where the map knows it.
        if (missing === null)
            void api.map.get().then(map => {
                location = map.locations.find(one => one.id === locationId) ?? null;
                if (!cancelled)
                    draw();
            }).catch(() => undefined);

        return () => { cancelled = true; };

    }

};
