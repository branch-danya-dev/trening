// Stable bridge codes; localized messages remain fallbacks for the current UI.
export const errorCodes=Object.freeze(['Validation','StaleConflict','CorruptOrFutureSchema','MissingAsset','UnsupportedCapability','InsufficientEvidence','PhotoQualityConflict','StorageQuota','UnsupportedBrowser','RecoveryRequired','ResearchDisabled','Unexpected']);
export function appError(code,message,cause){
    if(!errorCodes.includes(code))throw new TypeError('Unknown application error code');
    const error=new Error(`[${code}] ${message}`,{cause});error.code=code;return error;
}
export function storageError(error){
    return error?.name==='QuotaExceededError'?appError('StorageQuota','Недостаточно места. Сохраните backup и удалите ненужные производные изображения.',error):error;
}
