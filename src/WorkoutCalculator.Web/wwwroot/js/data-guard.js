// One synchronous guard shared by all persistent writers. Old tabs cannot overwrite a restored archive.
export const LOCK = 'trening:restore-in-progress', EPOCH = 'trening:data-generation';
let generation;
export function acceptGeneration() { generation = localStorage.getItem(EPOCH); }
export function assertWritable() {
    if (localStorage.getItem(LOCK)) throw Error('Восстановление данных ещё не завершено. Перезагрузите страницу.');
    if (generation !== undefined && generation !== localStorage.getItem(EPOCH))
        throw Error('Данные восстановлены в другой вкладке. Перезагрузите страницу перед изменениями.');
}
try { acceptGeneration(); } catch { /* Reads report the actual failure; never turn it into an empty backup. */ }
