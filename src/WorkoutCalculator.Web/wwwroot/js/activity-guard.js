// Central gate for ALL normal cardio/strength writers, including editors opened before closure.
// Backup restore has its own exclusive transaction and validates the complete source graph in C#.
export const activityKey = 'workoutcalc.activityDays.v1';
export const sourceKeys = ['workoutcalc.workouts.v1', 'workoutcalc.strength.v1'];
export function assertActivitySourceWrite(key, previous, next, activityRaw, today) {
    if (!sourceKeys.includes(key)) return;
    const cardio = key === sourceKeys[0];
    const rows = raw => { const v = JSON.parse(raw || 'null'); return cardio ? (v || []) : Array.isArray(v) ? v : (v?.sessions || []); };
    const oldRows = rows(previous), newRows = rows(next), id = r => cardio ? r.Id : r.id, date = r => cardio ? r.Date : r.date;
    let days = [];
    if (activityRaw !== null) {
        const env = JSON.parse(activityRaw), data = JSON.parse(env.payload);
        if (env.schemaVersion !== 1 || typeof env.sha256 !== 'string' || data.schemaVersion !== 1 || !Array.isArray(data.days))
            throw Error('Дни повреждены или имеют новую версию. Запись тренировок заблокирована.');
        days = data.days;
    }
    const locked = days.filter(d => d.state !== 'Open'), type = cardio ? 'Cardio' : 'Strength';
    const all = new Set([...oldRows, ...newRows].map(id));
    for (const keyId of all) {
        const before = oldRows.find(r => id(r) === keyId), after = newRows.find(r => id(r) === keyId);
        if (JSON.stringify(before) === JSON.stringify(after)) continue;
        if (after && date(after) > today) throw Error('Нельзя записывать фактическую тренировку в будущем.');
        if (locked.some(d => d.date === (before && date(before)) || d.date === (after && date(after)) ||
            d.actualEvents?.some(e => e.type === type && e.linkedEntityId === keyId)))
            throw Error('День уже завершён. Изменение связанных тренировок пока недоступно.');
    }
}
