#import "lib/sample-chart.typ": plotted-series, line-chart, evaluated-results

= Test Report
#v(0.5em)
#text(size: 14pt)[#sys.inputs.title]

#line(length: 100%)

== Summary
- *Run ID:* #sys.inputs.runId
- *Plan / Suite:* #sys.inputs.planName
- *Execution scope:* #sys.inputs.executionScope
- *DUT Serial:* #sys.inputs.dutSerial
- *Result:* #sys.inputs.result
- *Started:* #sys.inputs.startedAt
- *Completed:* #sys.inputs.completedAt
- *Sample count:* #sys.inputs.sampleCount
- *Software:* #sys.inputs.appVersion (#sys.inputs.appCommit)

== Notes
#sys.inputs.notes

#if sys.inputs.measurementOutcomes != "" [
  == Measurement validity
  #sys.inputs.measurementOutcomes
]

#if sys.inputs.executionScopeKind == "reference-acquisition" [
  #text(fill: orange)[*REFERENCE ACQUISITION ONLY*]
  Dependent reference acceptance was not evaluated. This is not production acceptance.
] else if sys.inputs.result == "Passed" [
  #text(fill: green)[*PASS*]
] else [
  #text(fill: red)[*FAIL*]
]

#if sys.inputs.includeReferenceEvidence == "true" [
  == Bench reference evidence
  #sys.inputs.referenceEvidence
]

#if sys.inputs.attestationDetail != "" [
  == Certified by
  The person who certifies this report may differ from the session operator.
  - *Party:* #sys.inputs.attestationDetail
  - *Presentation:* #sys.inputs.attestationKind
  #if sys.inputs.attestationAt != "" [
    - *At:* #sys.inputs.attestationAt
  ]
  Cryptographic proof, when used, is the detached attestation sidecar hashed to this PDF.
]

#if sys.inputs.includeHistory == "true" [
  == DUT history
  - *Severity:* #sys.inputs.historySeverity
  - #sys.inputs.historySummary
  #if sys.inputs.historyMetrics != "" [
    #sys.inputs.historyMetrics
  ]
]

#evaluated-results(json("run.json"))

#if sys.inputs.includePlots == "true" [
  #let run = json("run.json")
  #let series = plotted-series(run)
  #if series.len() > 0 [
    == Plots
    #for plot in series [
      #let label = if plot.unit == none or plot.unit == "" { plot.metric } else { plot.metric + " (" + plot.unit + ")" }
      #let caption = if plot.step == none or plot.step == "" { label } else { label + " · " + plot.step }
      #figure(line-chart(plot.values.enumerate(), title: label), caption: caption)
      #v(0.5em)
    ]
  ]
]

#if sys.inputs.attemptSummary != "" [
  == Step attempts
  #sys.inputs.attemptSummary
]
