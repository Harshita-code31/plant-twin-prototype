"use strict";
// Local DEMO retrieval only; safety-critical production answers require a separately validated backend.
(function(root, factory){
  const api=factory();
  if(typeof module==="object"&&module.exports)module.exports=api;
  if(root)root.ANUBHAV_RETRIEVAL=api;
})(typeof globalThis!=="undefined"?globalThis:null,function(){
  const ids={leak:"SOP-GC-004",trip:"SOP-CP-002",lel:"SOP-GT-003",ptw:"SOP-PTW-001",ppe:"SOP-PPE-005",communication:"SOP-EC-006"};
  const unsupported={en:"This isn’t covered in the SOPs — ask your shift supervisor.",hi:"यह SOP में शामिल नहीं है — अपने शिफ्ट सुपरवाइज़र से पूछें।"};
  const banner={en:"Training sample — NOT an approved plant procedure:",hi:"प्रशिक्षण नमूना — स्वीकृत साइट SOP नहीं:"};
  const noValue={en:"The sample SOP does not provide this exact operating setpoint. Do not guess; consult the plant’s approved procedure or shift supervisor.",hi:"नमूना SOP में इस संचालन सेटपॉइंट का सटीक मान नहीं दिया गया है। अनुमान न लगाएं; प्लांट की स्वीकृत प्रक्रिया या शिफ्ट सुपरवाइज़र से पूछें।"};
  const noLEL={en:"This sample SOP does not specify a numerical LEL alarm limit. Obtain the approved, site-specific detector setpoint and action matrix from the control room or shift supervisor.",hi:"इस नमूना SOP में LEL अलार्म की कोई संख्यात्मक सीमा नहीं दी गई है। स्वीकृत साइट-विशिष्ट सेटपॉइंट और कार्रवाई सूची कंट्रोल रूम या शिफ्ट सुपरवाइज़र से लें।"};
  function detectLanguage(q){
    if(/[\u0900-\u097F]/u.test(q))return "hi";
    return /\b(?:mere|meri|mujhe|kya|karu|karun|karoon|karna|krna|kaise|chahiye|hue|hui|hua|gaya|gayi|hone|hota|hoti|sabse|pehle|batao|bataiye|mein|hai|hain)\b/i.test(q)?"hi":"en";
  }
  function getIntent(question){
    const q=question.toLowerCase();
    if(/\b(?:leak|leaking|leakage|discharge\s+flange)\b|रिसाव|लीक/u.test(q))return "leak";
    if(/\blel\b|lower\s+explosive\s+limit|\bgas\s+(?:test|detect|monitor)\b|एलईएल|लेल|गैस\s+(?:जाँच|परीक्षण)/u.test(q))return "lel";
    if((/\b(?:compressor|compresor)\b|कंप्रेसर|कम्प्रेसर/u.test(q))&&(/\b(?:trip|tripped|tripping|stop|stopped|shutdown|shut\s+down|restart|start.up|reset|lube|filter)\b|ट्रिप|अचानक|बंद|रुक|रीस्टार्ट|फिल्टर|फ़िल्टर/u.test(q)))return "trip";
    if(/\b(?:ptw|permit|hot\s+work|work\s+authorization|confined\s+space)\b|परमिट/u.test(q))return "ptw";
    if(/\b(?:ppe|helmet|gloves|goggles|respirator|safety\s+shoes|personal\s+protective)\b|पीपीई|हेलमेट|दस्ताने/u.test(q))return "ppe";
    if(/\b(?:muster|warden|emergency\s+(?:report|communication|contact)|radio\s+channel)\b|मस्टर|इमरजेंसी\s+(?:संपर्क|सूचना)/u.test(q))return "communication";
    return null;
  }
  function sections(doc,numbers){return numbers.map(n=>doc.sections.find(s=>String(s.number)===String(n))).filter(Boolean);}
  function chosenSections(intent,doc,q){
    q=q.toLowerCase();
    if(intent==="trip")return sections(doc,/\b(?:lube|oil|filter)\b|ल्यूब|तेल|फिल्टर|फ़िल्टर/u.test(q)?[2,1]:/\b(?:restart|start.up|reset)\b|रीस्टार्ट/u.test(q)?[4,1]:[1,3]);
    if(intent==="leak")return sections(doc,/\b(?:blowdown|depressur)\b|ब्लोडाउन/u.test(q)?[1,3]:/\b(?:esd|isolat|shutdown)\b|आइसोलेशन|शटडाउन/u.test(q)?[1,2]:[1,4]);
    if(intent==="lel")return sections(doc,/\blel\b|\b(?:alarm|limit|setpoint|threshold)\b|एलईएल|लेल|अलार्म|सीमा/u.test(q)?[2]:[1,3]);
    return sections(doc,[1,2]);
  }
  function refs(doc,list){return list.map(s=>({id:doc.id,section:String(s.number),revision:doc.revision}));}
  function answer(q,docs,lang){
    lang=lang==="hi"?"hi":"en";
    const intent=getIntent(q),doc=Array.isArray(docs)&&intent?docs.find(d=>d.id===ids[intent]):null;
    if(!doc)return {answer:unsupported[lang],sources:[],mode:"refusal",intent:null};
    if((intent==="trip"||intent==="leak")&&/\b(?:pressure|temperature|rpm|speed|setpoint|threshold|psi|bar|torque)\b|दबाव|तापमान|सेटपॉइंट/u.test(q)&&/\b(?:exact|specific|value|number|how\s+much|what\s+is|what's|required|minimum|maximum|limit|setting|restart)\b|सटीक|कितना|सीमा/u.test(q)){
      return {answer:banner[lang]+"\n"+noValue[lang],sources:refs(doc,intent==="trip"?sections(doc,[4]):[]),mode:"missing-value",intent};
    }
    // A suspected active gas leak requires immediate, short actions first.
    // No assumption is made about the real location, gas type or compressor equipment.
    if(intent==="leak"){
      const cited=sections(doc,[1,4,2]);
      const guidance={
        en: [
          "⚠️ POSSIBLE GAS LEAK: If this is happening now, follow the site's emergency plan — do not rely on this training demo.",
          "1. Stop work and move away from the suspected leak. Do not approach, investigate or attempt repairs.",
          "2. From a safe location, raise the site emergency alarm and notify the control room of the location and observed hazard.",
          "3. Evacuate by the site's designated safe route, favouring upwind where safe. Report to the muster point and do not re-enter until cleared.",
          "4. Avoid ignition sources. Emergency shutdown and isolation are for authorised staff following the approved site procedure.",
          "Illustrative compressor-skid training sample only — not an approved plant-wide or gas-specific SOP."
        ].join("\n"),
        hi: [
          "⚠️ संभावित गैस रिसाव: अगर यह सच में अभी हो रहा है, साइट की आपात योजना अपनाएँ — इस प्रशिक्षण चैट पर निर्भर न रहें।",
          "1. काम तुरंत रोकें और रिसाव से दूर हटें। पास जाकर जाँच या मरम्मत न करें।",
          "2. सुरक्षित स्थान से आपातकालीन अलार्म दें और कंट्रोल रूम को स्थान व खतरे की सूचना दें।",
          "3. साइट के निर्धारित सुरक्षित रास्ते से निकलें; जहाँ सुरक्षित हो हवा की विपरीत दिशा चुनें। मस्टर पॉइंट पर उपस्थिति दर्ज करें और अनुमति तक वापस न जाएँ।",
          "4. आग या चिंगारी के स्रोतों से बचें। ESD और आइसोलेशन केवल अधिकृत कर्मचारी स्वीकृत प्रक्रिया से करें।",
          "यह कंप्रेसर स्किड का केवल प्रशिक्षण उदाहरण है, स्वीकृत प्लांट या गैस-विशिष्ट SOP नहीं।"
        ].join("\n")
      };
      return {answer:guidance[lang],sources:refs(doc,cited),mode:"emergency-demo",intent};
    }
    if(intent==="lel"&&/\blel\b|एलईएल|लेल/iu.test(q)&&/\b(?:limit|threshold|setpoint|alarm|value)\b|सीमा|अलार्म|कितना/iu.test(q)){
      return {answer:banner[lang]+"\n"+noLEL[lang],sources:refs(doc,sections(doc,[2])),mode:"missing-lel",intent};
    }
    const selected=chosenSections(intent,doc,q);
    if(!selected.length)return {answer:unsupported[lang],sources:[],mode:"refusal",intent:null};
    return {answer:banner[lang]+"\n"+selected.map((s,i)=>(i+1)+". "+(lang==="hi"?s.hi:s.en)).join("\n\n"),sources:refs(doc,selected),mode:"extractive",intent};
  }
  return {answer,detectLanguage,getIntent};
});
