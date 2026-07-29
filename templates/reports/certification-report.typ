#import "lib/sample-chart.typ": channel-names, samples-for, line-chart

= Certification Report
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
- *Software:* #sys.inputs.appVersion (#sys.inputs.appCommit)

== Notes
#sys.inputs.notes

#if sys.inputs.result == "Passed" [
  #text(fill: green)[*CERTIFIED PASS*]
] else [
  #text(fill: red)[*NOT CERTIFIED*]
]

#if sys.inputs.includePlots == "true" [
  #let run = json("run.json")
  #let channels = channel-names(run, max-count: 4)
  #if channels.len() > 0 [
    == Measurements
    #for ch in channels [
      #let pts = samples-for(run, ch)
      #if pts.len() > 0 [
        #figure(
          line-chart(pts, title: ch),
          caption: [Channel #ch],
        )
        #v(0.5em)
      ]
    ]
  ]
]

#if sys.inputs.attemptSummary != "" [
  == Step attempts
  #sys.inputs.attemptSummary
]
