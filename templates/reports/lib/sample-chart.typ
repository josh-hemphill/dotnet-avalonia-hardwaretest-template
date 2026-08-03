// Offline sample chart helpers for HardwareTest reports (no @preview packages).
//
// Parse run.json then chart a series:
//   #let run = json("run.json")
//   #line-chart(samples-for(run, "VDC"), title: "VDC")
// Pin to a step:
//   #line-chart(samples-for(run, "VDC", step-path: "Sample Hardware Suite/Acquire VDC"), title: "Acquire VDC")

#let sample-rows(run) = {
  let rows = run.at("samples", default: none)
  if rows == none {
    rows = run.at("Samples", default: ())
  }
  if type(rows) != array {
    return ()
  }
  rows
}

#let sample-field(s, camel, pascal) = {
  let v = s.at(camel, default: none)
  if v == none {
    v = s.at(pascal, default: "")
  }
  v
}

#let samples-for(run, channel, step-path: none) = {
  let rows = sample-rows(run)
  let filtered = rows.filter(s => {
    let ch = str(sample-field(s, "channel", "Channel"))
    if lower(ch) != lower(channel) {
      return false
    }
    if step-path == none {
      return true
    }
    return str(sample-field(s, "stepPath", "StepPath")) == step-path
  })
  filtered.enumerate().map(((i, s)) => (i, float(sample-field(s, "value", "Value"))))
}

#let channel-names(run, max-count: 4) = {
  let rows = sample-rows(run)
  if rows.len() == 0 {
    return ()
  }
  let seen = ()
  for s in rows {
    let ch = str(sample-field(s, "channel", "Channel"))
    if ch != "" and ch not in seen {
      seen.push(ch)
    }
    if seen.len() >= max-count {
      break
    }
  }
  seen
}

#let line-chart(points, title: "", width: 420pt, height: 150pt) = {
  if points.len() == 0 {
    return none
  }
  let ys = points.map(p => p.at(1))
  let min-y = calc.min(..ys)
  let max-y = calc.max(..ys)
  let span = if max-y == min-y { 1.0 } else { max-y - min-y }
  let pad = 18pt
  let plot-w = width - pad
  let plot-h = height - 28pt
  let n = points.len()
  let coords = points.enumerate().map(((i, p)) => {
    let x = if n <= 1 { 0.0 } else { i / (n - 1) }
    let y = (p.at(1) - min-y) / span
    (pad + x * plot-w, 12pt + (1 - y) * plot-h)
  })

  block(width: width, breakable: false)[
    #if title != "" [
      #text(weight: "semibold", size: 10pt)[#title]
      #v(4pt)
    ]
    #box(width: width, height: height, stroke: 0.4pt + luma(180), inset: 2pt)[
      #place(line(start: (pad, 12pt), end: (pad, 12pt + plot-h), stroke: 0.5pt + luma(120)))
      #place(line(
        start: (pad, 12pt + plot-h),
        end: (pad + plot-w, 12pt + plot-h),
        stroke: 0.5pt + luma(120),
      ))
      #for i in range(coords.len() - 1) {
        place(line(start: coords.at(i), end: coords.at(i + 1), stroke: 1.2pt + rgb("#1565C0")))
      }
      #place(dx: 2pt, dy: 0pt, text(size: 7pt, fill: luma(90))[#str(calc.round(max-y, digits: 3))])
      #place(dx: 2pt, dy: height - 14pt, text(size: 7pt, fill: luma(90))[#str(calc.round(min-y, digits: 3))])
    ]
  ]
}
