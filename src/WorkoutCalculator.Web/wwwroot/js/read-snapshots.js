// Bounded same-tab bridge handles for exact previously observed strings. Never an authority or persisted index.
import { EPOCH } from './data-guard.js';
let generation,sequence=0;const byKey=new Map();
function sync(){const current=localStorage.getItem(EPOCH);if(current!==generation){byKey.clear();generation=current;}}
export function rememberRead(key,value){
    sync();let items=byKey.get(key)||[];
    if(items[0]?.value===value)return items[0].token;
    const token=String(++sequence);items=[{token,value},...items].slice(0,2);byKey.set(key,items);
    // Only the application's finite set of keys may hold potentially large values.
    if(byKey.size>32)byKey.delete(byKey.keys().next().value);
    return token;
}
export function readToken(key){sync();return byKey.get(key)?.[0]?.token??'';}
export function resolveReads(tokens){
    sync();const expected={};
    for(const [key,token]of Object.entries(tokens)){
        const item=byKey.get(key)?.find(x=>x.token===token);if(!item)return null;expected[key]=item.value;
    }
    return expected;
}
