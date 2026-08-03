#import "lib/sample-chart.typ": channel-names, samples-for, line-chart

= Status Report
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
  #text(fill: green)[*PASS*]
] else [
  #text(fill: red)[*FAIL*]
]

#if sys.inputs.includeHistory == "true" [
  == DUT history
  - *Severity:* #sys.inputs.historySeverity
  - #sys.inputs.historySummary
  #if sys.inputs.historyMetrics != "" [
    #sys.inputs.historyMetrics
  ]
]

#if sys.inputs.includePlots == "true" [
  #let run = json("run.json")
  #let channels = channel-names(run, max-count: 4)
  #if channels.len() > 0 [
    == Plots
    #for ch in channels [
      #let pts = samples-for(run, ch)
      #if pts.len() > 0 [
        #figure(
          line-chart(pts, title: ch),
          caption: [Channel #ch (from run Samples)],
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
