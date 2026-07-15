= Test Report
#v(0.5em)
#text(size: 14pt)[#sys.inputs.title]

#line(length: 100%)

== Summary
- *Run ID:* #sys.inputs.runId
- *Plan / Suite:* #sys.inputs.planName
- *DUT Serial:* #sys.inputs.dutSerial
- *Result:* #sys.inputs.result
- *Started:* #sys.inputs.startedAt
- *Completed:* #sys.inputs.completedAt
- *Sample count:* #sys.inputs.sampleCount

== Notes
#sys.inputs.notes

#if sys.inputs.result == "Passed" [
  #text(fill: green)[*PASS*]
] else [
  #text(fill: red)[*FAIL*]
]

#if sys.inputs.includePlots == "true" [
  == Plots
  #let p0 = sys.inputs.at("plot0", default: "")
  #let p1 = sys.inputs.at("plot1", default: "")
  #let p2 = sys.inputs.at("plot2", default: "")
  #if p0 != "" [
    #image(p0, width: 80%)
  ]
  #if p1 != "" [
    #v(0.5em)
    #image(p1, width: 80%)
  ]
  #if p2 != "" [
    #v(0.5em)
    #image(p2, width: 80%)
  ]
]
