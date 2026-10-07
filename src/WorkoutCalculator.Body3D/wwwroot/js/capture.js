// Съёмка с подсказками. Телефон стоит неподвижно, человек отходит на 2–3 м. Модель позы MediaPipe
// (lib/mediapipe-1.1.0, считается в браузере) находит 33 точки тела, по ним проверяется кадр: весь ли
// человек виден, спереди он или боком, где руки, стоит ли неподвижно. Когда всё верно, через 3 секунды
// кадр снимается сам. Подсказки — на экране и голосом. Снимки никуда не отправляются: их сохраняет
// photos.js на этом устройстве, и только после «Сохранить».
import { saveSession } from './photos.js';
import { landmarker as poseModel } from './pose.js';

const INFER_INTERVAL_MS = 90;      // ~11 кадров в секунду: хватает для подсказок и не греет телефон
const INFER_SHARE = 0.5;           // распознавание занимает не больше половины времени — кнопки отзываются
const STILL_MS = 800;              // сколько стоять неподвижно перед отсчётом
const COUNTDOWN = 3;
const SPEAK_GAP_MS = 2500;         // не чаще одной фразы за это время
const REPEAT_MS = 7000;            // та же подсказка повторяется не чаще

const STEPS = [
    { view: 'front', title: 'Спереди', start: 'Встаньте лицом к камере во весь рост, руки чуть в стороны' },
    { view: 'side', title: 'Сбоку', start: 'Теперь повернитесь боком: руки вдоль тела, ноги вместе' },
];

// Точки BlazePose
const NOSE = 0, L_SHOULDER = 11, R_SHOULDER = 12, L_WRIST = 15, R_WRIST = 16, L_HIP = 23, R_HIP = 24;
const L_ANKLE = 27, R_ANKLE = 28, L_HEEL = 29, R_HEEL = 30, L_FOOT = 31, R_FOOT = 32;
const BONES = [[11, 12], [11, 13], [13, 15], [12, 14], [14, 16], [11, 23], [12, 24], [23, 24],
    [23, 25], [25, 27], [27, 29], [29, 31], [27, 31], [24, 26], [26, 28], [28, 30], [30, 32], [28, 32]];

// ---------- Проверка позы ----------

/**
 * Что не так с кадром: { ok, say } — say это подсказка, если не ok. lm — 33 точки в долях кадра,
 * w × h — размер кадра в пикселях (доли переводятся в пиксели, чтобы углы и пропорции были честными).
 */
export function assessPose(lm, view, w, h) {
    if (!lm) return { ok: false, say: 'Встаньте перед камерой во весь рост' };
    const pt = i => ({ x: lm[i].x * w, y: lm[i].y * h, v: lm[i].visibility ?? 1 });
    const mid = (a, b) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
    const shoulders = mid(pt(L_SHOULDER), pt(R_SHOULDER));
    const hips = mid(pt(L_HIP), pt(R_HIP));
    const torso = Math.hypot(shoulders.x - hips.x, shoulders.y - hips.y);
    const nose = pt(NOSE);
    // Макушка — выше носа примерно на 0,8 расстояния от носа до плеч
    const headTop = nose.y - 0.8 * Math.max(0, shoulders.y - nose.y);
    const feet = [L_HEEL, R_HEEL, L_FOOT, R_FOOT, L_ANKLE, R_ANKLE].map(pt);
    const feetSeen = feet.some(p => p.v > 0.5);
    const sole = Math.max(...feet.map(p => p.y));

    if (!feetSeen || sole > 0.985 * h) return { ok: false, say: 'Отойдите дальше: в кадре должны быть ступни' };
    if (headTop < 0.015 * h) return { ok: false, say: 'Отойдите дальше: в кадре должна быть вся голова' };
    if ((sole - headTop) < 0.55 * h) return { ok: false, say: 'Подойдите ближе к камере' };
    if (hips.x < 0.25 * w || hips.x > 0.75 * w) return { ok: false, say: 'Встаньте по центру кадра' };

    const shoulderSpan = pt(L_SHOULDER).x - pt(R_SHOULDER).x; // лицом к камере левое плечо — справа на кадре
    const turn = Math.abs(shoulderSpan) / torso;
    if (view === 'front') {
        if (turn < 0.5 || shoulderSpan < 0) return { ok: false, say: 'Повернитесь лицом к камере' };
        for (const [s, wr] of [[L_SHOULDER, L_WRIST], [R_SHOULDER, R_WRIST]]) {
            const a = pt(s), b = pt(wr);
            const angle = Math.atan2(Math.abs(b.x - a.x), b.y - a.y) * 180 / Math.PI;
            if (angle < 12) return { ok: false, say: 'Отведите руки чуть в стороны' };
            if (angle > 55) return { ok: false, say: 'Опустите руки ниже' };
        }
        const hipSpan = Math.abs(pt(L_HIP).x - pt(R_HIP).x);
        const ankleSpan = Math.abs(pt(L_ANKLE).x - pt(R_ANKLE).x);
        if (ankleSpan < 0.5 * hipSpan) return { ok: false, say: 'Поставьте ноги чуть шире' };
        if (ankleSpan > 2.5 * hipSpan) return { ok: false, say: 'Поставьте ноги чуть уже' };
    } else {
        if (turn > 0.3) return { ok: false, say: 'Повернитесь боком к камере' };
        const near = pt(L_WRIST).v >= pt(R_WRIST).v ? pt(L_WRIST) : pt(R_WRIST);
        if (Math.abs(near.x - hips.x) > 0.4 * torso) return { ok: false, say: 'Опустите руки вдоль тела' };
    }
    return { ok: true, say: 'Отлично, замрите' };
}

/** Насколько сдвинулись точки между кадрами, в долях длины туловища. */
function motion(prev, lm, w, h) {
    if (!prev) return Infinity;
    const keys = [NOSE, L_SHOULDER, R_SHOULDER, L_HIP, R_HIP, L_ANKLE, R_ANKLE, L_WRIST, R_WRIST];
    const torso = Math.hypot((lm[L_SHOULDER].x - lm[L_HIP].x) * w, (lm[L_SHOULDER].y - lm[L_HIP].y) * h) || 1;
    let sum = 0;
    for (const i of keys) sum += Math.hypot((lm[i].x - prev[i].x) * w, (lm[i].y - prev[i].y) * h);
    return sum / keys.length / torso;
}

// ---------- Голос и звук ----------

function makeVoice() {
    const synth = window.speechSynthesis;
    const voice = synth?.getVoices().find(v => v.lang?.toLowerCase().startsWith('ru')) ?? null;
    let lastText = '', lastAt = 0;
    let audio = null;
    try { audio = new AudioContext(); } catch { /* без звука */ }
    return {
        say(text, force = false) {
            const now = performance.now();
            if (!synth || (!force && (now - lastAt < SPEAK_GAP_MS || (text === lastText && now - lastAt < REPEAT_MS)))) return;
            lastText = text;
            lastAt = now;
            synth.cancel();
            const u = new SpeechSynthesisUtterance(text);
            u.lang = 'ru-RU';
            if (voice) u.voice = voice;
            synth.speak(u);
        },
        beep(freq = 880, ms = 120) {
            if (!audio) return;
            const osc = audio.createOscillator(), gain = audio.createGain();
            osc.frequency.value = freq;
            gain.gain.setValueAtTime(0.2, audio.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + ms / 1000);
            osc.connect(gain).connect(audio.destination);
            osc.start();
            osc.stop(audio.currentTime + ms / 1000);
        },
        prime() { // iOS разрешает звук и речь только после нажатия — «будим» их сразу в обработчике
            audio?.resume?.();
            if (synth) synth.speak(new SpeechSynthesisUtterance(''));
        },
        stop() { synth?.cancel(); audio?.close?.(); },
    };
}

// ---------- Интерфейс поверх приложения ----------

function el(tag, className, text) {
    const e = document.createElement(tag);
    if (className) e.className = className;
    if (text !== undefined) e.textContent = text;
    return e;
}

function buildUi() {
    const root = el('div', 'capture');
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-label', 'Съёмка');
    const stage = el('div', 'capture-stage');
    const video = el('video', 'capture-video');
    video.muted = true;
    video.playsInline = true;
    video.setAttribute('playsinline', '');
    const canvas = el('canvas', 'capture-overlay');
    const count = el('div', 'capture-count');
    stage.append(video, canvas, count);

    const top = el('div', 'capture-top');
    const steps = el('div', 'capture-steps');
    const stepEls = STEPS.map((s, i) => steps.appendChild(el('span', null, `${i + 1}. ${s.title}`)));
    const status = el('div', 'capture-status');
    status.setAttribute('aria-live', 'polite');
    top.append(steps, status);

    const review = el('div', 'capture-review');
    const bottom = el('div', 'capture-bottom');
    const cancel = el('button', 'btn', 'Отмена');
    const flip = el('button', 'btn capture-flip', 'Сменить камеру');
    const shoot = el('button', 'btn capture-shoot', 'Снять сейчас');
    shoot.title = 'Снять, не дожидаясь проверки позы';
    const retake = el('button', 'btn capture-retake', 'Переснять');
    const save = el('button', 'btn primary capture-save', 'Сохранить');
    bottom.append(cancel, flip, shoot, retake, save);
    root.append(stage, top, review, bottom);
    document.body.append(root);
    return { root, stage, video, canvas, count, stepEls, status, review, cancel, flip, shoot, retake, save };
}

/** Где на экране кадр видео (object-fit: contain): для рисования поверх него. */
function contentRect(video) {
    const box = video.getBoundingClientRect();
    const vw = video.videoWidth || 1, vh = video.videoHeight || 1;
    const scale = Math.min(box.width / vw, box.height / vh);
    return { x: (box.width - vw * scale) / 2, y: (box.height - vh * scale) / 2, scale, width: box.width, height: box.height };
}

function draw(ui, lm, ok) {
    const { canvas, video } = ui;
    const r = contentRect(video);
    const dpr = window.devicePixelRatio || 1;
    if (canvas.width !== Math.round(r.width * dpr) || canvas.height !== Math.round(r.height * dpr)) {
        canvas.width = Math.round(r.width * dpr);
        canvas.height = Math.round(r.height * dpr);
    }
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, r.width, r.height);
    const vw = video.videoWidth, vh = video.videoHeight;
    const X = x => r.x + x * vw * r.scale, Y = y => r.y + y * vh * r.scale;
    // Поля кадра, за которые не должны заходить голова и ступни
    ctx.strokeStyle = 'rgba(255,255,255,0.25)';
    ctx.setLineDash([6, 6]);
    ctx.lineWidth = 1;
    for (const y of [0.015, 0.985]) {
        ctx.beginPath();
        ctx.moveTo(X(0), Y(y));
        ctx.lineTo(X(1), Y(y));
        ctx.stroke();
    }
    ctx.setLineDash([]);
    if (!lm) return;
    ctx.strokeStyle = ok ? '#3dd6c6' : '#f0b75a';
    ctx.fillStyle = ctx.strokeStyle;
    ctx.lineWidth = 3;
    for (const [a, b] of BONES) {
        if ((lm[a].visibility ?? 1) < 0.3 || (lm[b].visibility ?? 1) < 0.3) continue;
        ctx.beginPath();
        ctx.moveTo(X(lm[a].x), Y(lm[a].y));
        ctx.lineTo(X(lm[b].x), Y(lm[b].y));
        ctx.stroke();
    }
}

/** Кадр видео целиком, в исходном разрешении камеры, без зеркала. */
function grab(video) {
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d').drawImage(video, 0, 0);
    return new Promise((resolve, reject) =>
        canvas.toBlob(b => b ? resolve(b) : reject(new Error('Не удалось снять кадр')), 'image/jpeg', 0.92));
}

async function openCamera(facingMode) {
    if (!navigator.mediaDevices?.getUserMedia) {
        throw new Error('Камера доступна только на сайте по HTTPS. Откройте опубликованную версию приложения или загрузите готовые фото.');
    }
    try {
        return await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: { facingMode, width: { ideal: 1920 }, height: { ideal: 1920 } },
        });
    } catch (e) {
        if (e?.name === 'NotAllowedError') throw new Error('Нет доступа к камере: разрешите его для этого сайта в настройках браузера.');
        if (e?.name === 'NotFoundError' || e?.name === 'OverconstrainedError') throw new Error('Камера не найдена.');
        throw new Error('Не удалось включить камеру: ' + (e?.message ?? e));
    }
}

// ---------- Съёмка ----------

/**
 * Съёмка спереди и сбоку. metaJson — замеры на момент съёмки (как у photos.saveSession).
 * Возвращает JSON сохранённой сессии или пустую строку, если съёмку отменили (кнопкой или Esc — на любом этапе).
 */
export function run(metaJson) {
    return new Promise((resolve, reject) => {
        const voice = makeVoice();
        voice.prime();
        const ui = buildUi();
        let stream = null, raf = 0, wakeLock = null, done = false;
        let facing = 'user'; // фронтальная камера: человеку видно экран и подсказки
        const shots = {};
        const urls = [];

        const setStatus = text => { ui.status.textContent = text; };
        const showStep = i => ui.stepEls.forEach((e, j) => e.classList.toggle('on', i === j));
        const mode = m => { ui.root.dataset.mode = m; }; // loading | aim | review — от него зависят кнопки

        const onKey = e => { if (e.key === 'Escape') finish(''); };
        const finish = (value, error) => {
            if (done) return;
            done = true;
            cancelAnimationFrame(raf);
            stream?.getTracks().forEach(t => t.stop());
            wakeLock?.release?.().catch(() => { });
            voice.stop();
            urls.forEach(URL.revokeObjectURL);
            document.removeEventListener('keydown', onKey);
            ui.root.remove();
            if (error) reject(error);
            else resolve(value);
        };
        document.addEventListener('keydown', onKey);
        ui.cancel.onclick = () => finish('');

        const startCamera = async () => {
            stream?.getTracks().forEach(t => t.stop());
            setStatus('Включаю камеру…');
            stream = await openCamera(facing);
            if (done) { stream.getTracks().forEach(t => t.stop()); return; }
            ui.video.srcObject = stream;
            ui.stage.classList.toggle('mirror', facing === 'user');
            await ui.video.play();
        };

        (async () => {
            mode('loading');
            setStatus('Загружаю распознавание позы…');
            const landmarker = await poseModel('video', p => setStatus(`Загружаю распознавание позы… ${Math.round(p * 100)} %`));
            if (done) return;
            await startCamera();
            if (done) return;
            try { wakeLock = await navigator.wakeLock?.request('screen'); } catch { /* экран может погаснуть — не страшно */ }

            let step = 0, prev = null, stillSince = 0, countdownAt = 0, lastInfer = 0, gap = INFER_INTERVAL_MS, lastShown = '';

            const beginStep = i => {
                step = i;
                prev = null;
                stillSince = 0;
                countdownAt = 0;
                lastShown = '';
                ui.count.textContent = '';
                showStep(i);
                mode('aim');
                setStatus(STEPS[i].start);
                voice.say(STEPS[i].start, true);
            };

            const showReview = () => {
                cancelAnimationFrame(raf);
                mode('review');
                ui.count.textContent = '';
                setStatus('Проверьте снимки: видно ли тело целиком, от макушки до ступней?');
                voice.say('Готово. Проверьте снимки', true);
                ui.review.replaceChildren(...STEPS.filter(s => shots[s.view]).map(s => {
                    const url = URL.createObjectURL(shots[s.view]);
                    urls.push(url);
                    const figure = el('figure');
                    const img = el('img');
                    img.src = url;
                    img.alt = s.title;
                    figure.append(img, el('figcaption', null, s.title));
                    return figure;
                }));
            };

            const takeShot = async () => {
                shots[STEPS[step].view] = await grab(ui.video);
                voice.beep(1320, 180);
                if (step + 1 < STEPS.length) beginStep(step + 1);
                else showReview();
            };

            const tick = now => {
                if (done) return;
                raf = requestAnimationFrame(tick);
                const v = ui.video;
                if (ui.root.dataset.mode !== 'aim' || v.readyState < 2 || now - lastInfer < gap) return;
                lastInfer = now;
                const lm = landmarker.detectForVideo(v, now).landmarks?.[0] ?? null;
                // На слабом устройстве распознавание медленное — реже, чтобы нажатия не ждали
                gap = Math.max(INFER_INTERVAL_MS, (performance.now() - now) / INFER_SHARE);
                const verdict = assessPose(lm, STEPS[step].view, v.videoWidth, v.videoHeight);
                const moving = lm ? motion(prev, lm, v.videoWidth, v.videoHeight) > 0.02 : true;
                prev = lm;
                draw(ui, lm, verdict.ok && !moving);

                if (!verdict.ok || moving) {
                    stillSince = 0;
                    if (countdownAt) { countdownAt = 0; ui.count.textContent = ''; }
                    const text = verdict.ok ? 'Замрите' : verdict.say;
                    if (text !== lastShown) { setStatus(text); lastShown = text; }
                    voice.say(text);
                    return;
                }
                stillSince ||= now;
                if (!countdownAt && now - stillSince >= STILL_MS) {
                    countdownAt = now;
                    setStatus('Отлично, не двигайтесь');
                    lastShown = '';
                }
                if (countdownAt) {
                    const left = COUNTDOWN - Math.floor((now - countdownAt) / 1000);
                    if (left <= 0) {
                        countdownAt = 0;
                        ui.count.textContent = '';
                        mode('shooting'); // пока сохраняется кадр, проверка не идёт
                        takeShot().catch(e => finish(null, e));
                    } else if (ui.count.textContent !== String(left)) {
                        ui.count.textContent = String(left);
                        voice.beep(880, 100);
                        voice.say(['', 'один', 'два', 'три'][left] ?? '', true);
                    }
                }
            };

            ui.flip.onclick = () => {
                facing = facing === 'user' ? 'environment' : 'user';
                startCamera().catch(e => finish(null, e));
            };
            ui.shoot.onclick = () => { mode('shooting'); takeShot().catch(e => finish(null, e)); };
            ui.retake.onclick = () => {
                for (const k of Object.keys(shots)) delete shots[k];
                beginStep(0);
                raf = requestAnimationFrame(tick);
            };
            ui.save.onclick = async () => {
                ui.save.disabled = true;
                setStatus('Сохраняю…');
                try {
                    const session = await saveSession(JSON.parse(metaJson), shots);
                    finish(JSON.stringify(session));
                } catch (e) {
                    finish(null, e);
                }
            };

            beginStep(0);
            raf = requestAnimationFrame(tick);
        })().catch(e => finish(null, e));
    });
}
