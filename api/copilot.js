"use strict";
// Vercel serverless API. Run locally with "vercel dev"; NEVER send ANTHROPIC_API_KEY to client.
const docs=[
require("../web/copilot/sops/gas-leak.json"),
require("../web/copilot/sops/compressor-trip.json"),
require("../web/copilot/sops/permit-to-work.json"),
require("../web/copilot/sops/gas-testing.json"),
require("../web/copilot/sops/ppe.json"),
require("../web/copilot/sops/emergency-communications.json")
];
const translations={"कंप्रेसर":"compressor","कम्प्रेसर":"compressor","ट्रिप":"trip","गैस":"gas","लीक":"leak","रिसाव":"leak","अलार्म":"alarm","सीमा":"limit","लेल":"lel","एलईएल":"lel","कितना":"limit","परमिट":"permit","पीपीई":"ppe","सुरक्षा":"safety","आपातकाल":"emergency","फ़िल्टर":"filter","फिल्टर":"filter","हेलमेट":"helmet","निकासी":"evacuation","काम":"work"};
const unsupported={en:"This isn’t covered in the SOPs — ask your shift supervisor.",hi:"यह SOP में शामिल नहीं है — अपने शिफ्ट सुपरवाइज़र से पूछें।"};
function normalize(q){let x=q.toLowerCase();for(const [hi,en] of Object.entries(translations))x=x.split(hi).join(" "+en+" ");return x;}
function tokenize(s){return s.toLowerCase().replace(/[^\p{L}\p{N}\- ]/gu," ").split(/\s+/).filter(x=>x.length>2&&!["what","the","and","for","how","first","please","this","that","with","does","today"].includes(x));}
function retrieve(question){const terms=tokenize(normalize(question));let found=[];for(const doc of docs)for(const s of doc.sections){let score=0;for(const term of terms){if(s.heading.toLowerCase().includes(term))score+=4;if(doc.title.toLowerCase().includes(term))score+=3;if(doc.keywords.join(" ").includes(term))score+=2;if(s.en.toLowerCase().includes(term))score++;}if(score)found.push({doc,s,score});}found.sort((a,b)=>b.score-a.score);if(!found.length)return [];return found.filter(x=>x.score>=Math.max(3,found[0].score*.4)).slice(0,4);}
function sources(found){return found.map(x=>({id:x.doc.id,section:x.s.number,revision:x.doc.revision}));}
function deterministic(found,lang){return (lang==="hi"?"प्रशिक्षण नमूना — स्वीकृत साइट SOP नहीं:\n":"Training sample — NOT an approved plant procedure:\n")+found.map((x,i)=>(i+1)+". "+(lang==="hi"?x.s.hi:x.s.en)).join("\n\n");}
module.exports=async function handler(req,res){
res.setHeader("Cache-Control","no-store");res.setHeader("X-Content-Type-Options","nosniff");
if(req.method!=="POST"){res.setHeader("Allow","POST");return res.status(405).json({error:"Method not allowed"});}
let body=req.body;if(typeof body==="string"){try{body=JSON.parse(body)}catch{return res.status(400).json({error:"Invalid JSON"})}}
const question=body?.question,lang=body?.lang==="hi"?"hi":"en";
if(typeof question!=="string"||!question.trim()||question.length>1000)return res.status(400).json({error:"Question must be 1–1000 characters"});
const found=retrieve(question);
if(!found.length)return res.status(200).json({answer:unsupported[lang],sources:[],mode:"refusal"});
const cites=sources(found);
if(!process.env.ANTHROPIC_API_KEY)return res.status(200).json({answer:deterministic(found,lang),sources:cites,mode:"retrieval-demo"});
const supplied=found.map(x=>JSON.stringify({id:x.doc.id,revision:x.doc.revision,section:x.s.number,title:x.doc.title,heading:x.s.heading,english:x.s.en,hindi:x.s.hi,label:x.doc.label})).join("\n");
const system="You are ANUBHAV, a worker training COPILOT, not an operational safety authority. Use ONLY the cited source snippets provided as JSON. Do not invent facts, instructions, thresholds, alarm values, or equipment setpoints. All these SOPs are illustrative, never approved. If the question cannot actually be answered from snippets, output EXACTLY the refusal message for the requested language. For emergency queries lead with concise numbered immediate safety steps and tell users to follow site emergency instructions. The LEL numerical alarm threshold is NOT provided, so do NOT guess any number. Output natural Hindi in Devanagari for hi, or English for en. You may summarize but not expand beyond source text. Include in the answer that this is a training sample and site-specific approval is required. Do not generate citation identifiers; citations are attached separately by the server.";
try{
const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),16000);
let response;try{response=await fetch("https://api.anthropic.com/v1/messages",{method:"POST",signal:controller.signal,headers:{"content-type":"application/json","x-api-key":process.env.ANTHROPIC_API_KEY,"anthropic-version":"2023-06-01"},body:JSON.stringify({model:process.env.ANTHROPIC_MODEL||"claude-sonnet-4-5",max_tokens:650,temperature:0,system,messages:[{role:"user",content:"Language: "+lang+"\nQuestion: "+question+"\nPermitted SOP extracts:\n"+supplied}]})});}finally{clearTimeout(timer)}
if(!response.ok)throw Error("Provider request failed "+response.status);
const result=await response.json();const answer=(result.content||[]).filter(x=>x.type==="text").map(x=>x.text).join("\n").trim();
if(!answer)throw Error("Empty provider response");
if(answer.includes(unsupported[lang]))return res.status(200).json({answer:unsupported[lang],sources:[],mode:"refusal"});
return res.status(200).json({answer,sources:cites,mode:"claude"});
}catch(e){console.error("Claude unavailable:",e.message);return res.status(200).json({answer:deterministic(found,lang),sources:cites,mode:"retrieval-fallback"});}
};