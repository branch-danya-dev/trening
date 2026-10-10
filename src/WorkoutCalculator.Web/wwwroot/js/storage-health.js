import { CHECKIN_PENDING, LOCK } from './data-guard.js';
export async function storageHealth(){
    let estimate=null,persistent=null;
    try{estimate=await navigator.storage?.estimate?.();}catch{}
    try{persistent=await navigator.storage?.persisted?.()??null;}catch{}
    let localBytes=null,recoveryRequired=false,available=true;
    try{
        localBytes=0;
        for(let i=0;i<localStorage.length;i++){const k=localStorage.key(i);localBytes+=2*(k.length+(localStorage.getItem(k)?.length||0));}
        recoveryRequired=!!(localStorage.getItem(CHECKIN_PENDING)||localStorage.getItem(LOCK));
    }catch{available=false;}
    // estimate() describes origin storage, not the separate implementation-defined localStorage ceiling.
    const usage=Number.isFinite(estimate?.usage)?estimate.usage:null,quota=Number.isFinite(estimate?.quota)?estimate.quota:null;
    return {available,persistent,usage,quota,localBytes,recoveryRequired,
        backupRecommended:!available||recoveryRequired||persistent!==true||(quota>0&&usage/quota>=.8),
        lastSuccessfulBackup:null,lastBackupTracked:false,
        reason:!available?'UnsupportedBrowser':recoveryRequired?'RecoveryRequired':quota>0&&usage/quota>=.8?'StorageQuota':persistent!==true?'BackupRecommended':'Healthy'};
}
export async function storageHealthJson(){return JSON.stringify(await storageHealth());}
