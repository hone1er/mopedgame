# REFERENCES_VINTAGE_MOPEDS.md — Vintage Moped Reference Sheet & Asset Guide

> **Scope & Grounding Note**: This reference sheet supports **MopedGame** (`M5` Blender `.glb` kit, `data/catalogs/` flavor text & physical plausibility, and future roadmap archetypes) while strictly preserving canonical `meshAssetPath` strings, `SOCKET_*` / `BOLT_*` node names, and trademark-safe homage naming.
>
> **Retrieval Note**: Direct shell HTTP fetching (`curl`) was unavailable under sandbox command approval timeouts, and client-side SPA modal details on `pedalcodearmy.com/stable` were not fully indexed as static HTML snippets. All facts below are grounded strictly in `search_web` observations of `https://www.pedalcodearmy.com/stable` (and its `pedalcodearmy.vercel.app` mirror) plus verified vintage moped specification references cited in Section 5.

---

## 1. Observed Fleet & Culture on `pedalcodearmy.com/stable`

**Pedal Code Army** ([pedalcodearmy.com](https://pedalcodearmy.com/stable) / [pedalcodearmy.vercel.app](https://pedalcodearmy.vercel.app)) is an East Bay (Oakland / San Francisco Bay Area, California) moped crew and community hub founded by Joe Villavicencio, originating from skateboarders turned vintage 2-stroke builders and riders.

### Community & Garage Culture Touchstones (Directly Observed)
- **The Stable (`/stable`)**: Fleet showcase cataloging **24+ listed entries (27+ referenced builds)** across an East Bay core fleet and an international European Division (France & Germany), filterable by make, owner, and build specs.
- **Weekly Wobbles**: Routine Bay Area community events including *Moped Mondays* (spontaneous East Bay evening street cruises and taco-truck runs), *Tinkerer's Paradise* (collaborative garage sessions to fix rattling parts), and *East Bay Explorer*.
- **Custom 3D-Printed Parts Shop**: Features custom 3D-printed polymer moped parts—specifically **3D-printed Puch Magnum side covers** (PETG/ABS matte paint-ready shells with embossed text options and nylon quarter-turn mounting knobs).
- **East Bay Runner**: Browser mini-game on the site featuring urban traffic dodging and wheelies.

### Full Observed Roster in "The Stable"

| Division / Region | Stable Entry Title | Make / Model | Owner | Observed Visual / Build Cue |
| :--- | :--- | :--- | :--- | :--- |
| **Oakland / East Bay** | `Magnum II` | Puch Magnum II | Joe Villavicencio | Top-tank mini-motorcycle silhouette |
| **Oakland / East Bay** | `Derbi Variant TT` | Derbi Variant TT | Joe Villavicencio | Trail/scrambler stance, high-rise braced bars |
| **Oakland / East Bay** | `Derbi Variant SL '79` | Derbi Variant SL (1979) | Joe Villavicencio | Early piston-port Spanish step-through |
| **Oakland / East Bay** | `Derbi Variant SL '86` | Derbi Variant SL (1986) | Joe Villavicencio | Later flat-reed variated Variant SL |
| **Oakland / East Bay** | `Derbi RD 50` | Derbi RD 50 | Joe Villavicencio | Tubular steel cradle mini-moto/moped hybrid |
| **Oakland / East Bay** | `Maxi N` | Puch Maxi N | Joe Villavicencio | Rigid hardtail pressed-steel step-through |
| **Oakland / East Bay** | `Little Yellow Maxi` | Puch Maxi | Joe Villavicencio | Signature bright yellow paint colorway |
| **Oakland / East Bay** | `Hobbit PA 50I` | Honda Hobbit PA50I | Joe Villavicencio | Early restricted US PA50 |
| **Oakland / East Bay** | `Hobbit PA 50II` | Honda Hobbit PA50II | Joe Villavicencio | Variated V-matic PA50II |
| **Oakland / East Bay** | `Hobbit PA 50II (CRASHED - RIP)` | Honda Hobbit PA50II | Joe Villavicencio | Memorialized crash-damaged build tag |
| **Oakland / East Bay** | `Garelli SSXL LTD` | Garelli SSXL LTD | Joe Villavicencio | Italian sport top-tank, cast alloy mag wheels |
| **Oakland / East Bay** | `Maxi` | Puch Maxi | Joshua Willing | Classic E50 commuter / spare-parts build |
| **Oakland / East Bay** | `Hobbit` | Honda Hobbit | Isabel Muñoz | Step-through PA50 street rider |
| **Oakland / East Bay** | `Dirt Hobbit` | Honda Hobbit (Custom) | Alex Baranda | Off-road/scrambler mod (knobbies, tall bars) |
| **Oakland / East Bay** | `Maxi` | Puch Maxi | Lydia Vieyra | Classic step-through Puch build |
| **Euro Div. — France** | `Maxi S` | Puch Maxi S | Driss | Full-suspension swingarm Maxi w/ rear shocks |
| **Euro Div. — France** | `Maxi (Silver)` | Puch Maxi | Driss | Metallic silver colorway |
| **Euro Div. — France** | `Maxi (Black)` | Puch Maxi | Driss | Black colorway |
| **Euro Div. — France** | `Maxi (Grey)` | Puch Maxi | Driss | Slate/primer grey colorway |
| **Euro Div. — France** | `Puch Magnum` | Puch Magnum | Driss | Classic European top-tank Magnum |
| **Euro Div. — France** | `Puch Magnum (Red)` | Puch Magnum | Driss | Red colorway |
| **Euro Div. — France** | `X-10s` | Puch X-10s | Driss | Puch-Avello X-series full-suspension moped |
| **Euro Div. — Germany** | `KTM Hobby & Piaggio Ciao` | KTM Hobby / Piaggio Ciao | Nils Rösner | Austrian stamped commuter & Italian rigid monocoque |
| **Euro Div. — Germany** | `Zündapp Bergsteiger / 434` | Zündapp Bergsteiger M50 (Typ 434) | Nils Rösner | Heavy pressed-steel backbone mofa/moped |

---

## 2. Proportions, Silhouettes & Chassis Reference Data

To match real vintage moped silhouettes while honoring `PROJECT_GUIDE.md` §5.1 metric rules (`+Y` up, `+Z` forward, `1 unit = 1 m`, target complete wheelbase `1.12 m – 1.22 m`, saddle height `0.74 m – 0.82 m`):

### Chassis Geometry & Wheel Reference Matrix

| Real Reference Model | Game Archetype Mapping | Frame Silhouette | Real Wheelbase | Real Saddle Height / Clearance | Steering Rake (Typ.) | Wheel & Tire Outer Diameter | Tank Volume & Form |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Puch Maxi N / Maxi S** | `arch_maxer_e50` (**Maxer E50**) | Stamped pressed-steel step-through backbone; Maxi N is rigid hardtail, Maxi S adds rear swingarm + dual shocks | `1.100 – 1.120 m` (43.3–44.1 in) | `~0.100 m` clearance; saddle adjustable (`~0.75–0.80 m` in normal moped stance) | `~25°–27°` | `17"` rims (`2.00` or `2.25 x 17"`): **OD `0.556 – 0.565 m`** | `~3.2 L` integrated inside stamped central spine tube |
| **Honda Hobbit PA50I / PA50II** | `arch_bramble_pa50` (**Bramble PA50**) | Low step-through tube spine + prominent rear subframe/swingarm & long left-side **CVT belt cover**; horizontal forward cylinder | `1.064 m` (41.9 in); `1.670 m` overall length | `0.760 – 0.780 m` saddle; `~0.115–0.127 m` clearance | `~26°` | `17"` wire-spoke (`2.25 - 17`): **OD `0.556 – 0.565 m`** | `~3.0 L` (`~0.8 gal`) compact under-saddle/backbone tank |
| **Tomos A35 (Sprint / Targa)** | `arch_targa_a35` (**Targa A35**) | Dual aesthetic: `Sprint` step-through tube or `Targa` structural **top-tank ("gasket-tank")** horizontal spine over open engine cradle | `1.080 – 1.130 m` (42.5–44.5 in) | `~0.785 m` saddle; `~0.115 m` clearance | `~25°–28°` | `16"` rims (`2.25 - 16`): **OD `0.530 – 0.540 m`** (chunkier, lower stance) | `~3.5–4.0 L` integrated frame spine or top-mounted tank |

### Key Visual Proportion Rules for `tools/blender/`
1. **Normalized Wheelbase vs. Wheel Size**:
   - Real mopeds sit between `1.06 m` and `1.13 m` wheelbase; normalizing v1 frame models to **`1.12 m – 1.18 m`** (`Maxer E50` ~`1.14 m`, `Bramble PA50` ~`1.12 m` compact agile wheelbase, `Targa A35` ~`1.16 m`) satisfies `PROJECT_GUIDE.md` §5.1 while preserving authentic visual ratios.
   - **`wheel_16_spoke.glb` & `wheel_16_mag.glb`**: Model outer tire diameter at **`0.535 m` (`radius ~0.2675 m`)** for 16" spec (`2.25-16`), with `~0.057 m` tire cross-section width and `0.09–0.11 m` central drum brake hub. Place `SOCKET_FRONT_AXLE` / `SOCKET_REAR_AXLE` at `Y ≈ 0.268 m` relative to ground contact.
2. **Fork Rake & Trail**:
   - Telescopic front forks (`fork_stock_e50.glb`) lean back at **`25° – 27°`** from vertical with twin narrow chrome stanchions (`~26–28 mm` tubes, `~0.14–0.16 m` spacing) and lower painted/cast sliders holding a U-shaped fender mount (`SOCKET_FENDER_FRONT`) and twin fork-ear tabs (`SOCKET_HEADLIGHT_MOUNT`).
3. **Engine Cradle Placement (`SOCKET_ENGINE_MOUNT`)**:
   - **`case_e50.glb`**: Compact clamshell crankcase tucked low under the step-through spine; horizontal-angled cylinder projecting forward-up (`~35°–45°` above horizontal), protruding round wet-clutch cover on right/left, pedal crank spindle running transversely through lower case.
   - **`case_pa50.glb`**: Distinct **elongated rear-extending CVT pulley housing** (oval stamped metal/plastic belt cover running from crank back toward rear wheel hub) + horizontal forward cylinder + **reed block pad** on the rear/upper intake side.
   - **`case_a35.glb`**: Boxier squared-off aluminum crankcase housing the 2-speed clutch pack + kick/pedal shaft, angled upward cylinder (~30°–40°), square Tomos-style right/left side covers.
4. **Racks, Milk Crates & Colorways / Patina**:
   - **Colorway Palette (Observed on PCA Stable + Bay Area Aesthetic)**:
     - *Little Yellow* (`#E8B923` mustard/school-bus gloss enamel)
     - *Magnum Red* (`#C42828` cherry red with black/gold trim)
     - *Oakland Silver / Slate Grey / Matte Black* (`#B5BAC0`, `#5C646B`, `#1F2226`)
     - *3D-Printed PETG/ABS Accent* (matte charcoal polymer side covers with textured layer-line sheen and quarter-turn latches)
     - *Hardware Finishes*: Cast aluminum (satin gray), dark cast iron (charcoal rough), raw welded mild steel (gunmetal with heat-blued cone seams on expansion pipes), chrome (polished handlebars, CEV light bezel, rear shock springs), and oxidized brass/copper crush gaskets.

---

## 3. Canonical `.glb` Asset & Part Visual / Naming Guide

Every suggestion below keeps canonical filenames, `meshAssetPath`, and `SOCKET_*` / `BOLT_*` identifiers **100% unchanged** while guiding geometry silhouettes (`@blender-assets`) and flavor text / homage names (`@data-catalogs`).

### A. Cylinders, Heads & Pistons (`Models/Parts/Engine/`)
- **`cyl_stock_50.glb` & `head_stock.glb`**:
  - *Real Inspiration*: Puch 7-fin / 8-fin 48.8cc (`38 mm × 43 mm`) stock cylinder & flat-profile head; Honda PA50I/II 49cc stock barrel.
  - *Visual Guidance*: Compact square fin stack (**7–8 tight fins**), modest intake/exhaust spigots, low-profile square head with angled plug boss (`SOCKET_SPARK_PLUG`).
  - *Homage Naming / Flavor*: `"OEM 49cc Commuter Barrel"` — *"Seven tight cast fins tuned for mild 30 mph neighborhood puttering."*
- **`cyl_kit_70.glb` & `head_highcomp_70.glb`**:
  - *Real Inspiration*: **Airsal 45mm Nikasil**, **Polini *Fonte* 46mm cast-iron**, **Athena 45–47.6mm alloy**, and **Gilardoni 47mm 74cc** big-bore kits.
  - *Visual Guidance*: Noticeably wider/taller cylinder block (**9–10 deep radial/squared cooling fins**), beefier transfer bulges on the cylinder base, oversized exhaust flange ears (`BOLT_HEAD_STUD_1..4` corners). `head_highcomp_70.glb` adds deeper finning and a thicker squished dome crown.
  - *Homage Naming / Flavor*:
    - Cast-iron street kit flavor: *"Fonte 70 Street Torque Kit — Heavy cast-iron sleeve built to pull Twin Peaks without whining; forgiving enough to hone after a soft seize."*
    - Alloy race kit flavor: *"Corsa 70 Nikasil Kit — Deep alloy fins shed heat on long Bay Bridge WOT pulls; demands a #70+ main jet."*

### B. Carburetors, Intakes & Air Filters (`Models/Parts/Fuel/`)
- **`carb_bing_12.glb`**:
  - *Real Inspiration*: **Bing 1/12 (12mm)** horizontal slide carburetor (`#52–#66` jets).
  - *Visual Guidance*: Smooth rounded German-style cast aluminum body with its signature **cylindrical vertical screw-on bottom float bowl** and stubby pinch-clamp spigot (`BOLT_CARB_CLAMP`).
  - *Homage Naming*: `"Bavaria 12mm Round-Bowl Carb"`
- **`carb_sha_15.glb`**:
  - *Real Inspiration*: **Dellorto SHA 14.12 / 15.15** flat-slide single-jet carb (`#53–#70` M5 jets).
  - *Visual Guidance*: Compact boxy cast body, top straight cable tower, protruding spring choke lever on side, and a **wide round rear air-filter collar (`~59 mm` scaled proportionally)** matching `SOCKET_AIR_FILTER`.
  - *Homage Naming*: `"Milano SHA-15 Box Carb"` — *"Single main jet, zero needle fuss, and a stubborn thumb choke—the Bay Area daily commuter workhorse."*
- **`carb_phbg_19.glb`**:
  - *Real Inspiration*: **Dellorto PHBG 19mm** round-slide tunable race carb (main, pilot, choke jets + 5-clip needle).
  - *Visual Guidance*: Taller, rounder Italian racing body with knurled threaded top cap, external brass/steel spring-loaded idle & air-mixture screws protruding laterally, and a deeper bolted/clipped float bowl with drain nipple.
  - *Homage Naming*: `"Corsa 19mm Triple-Circuit Carb"` — *"External idle/air screws and a 5-clip needle let you chase clean WOT chops from sea level to Grizzly Peak."*
- **`intake_stock.glb` vs. `intake_reed_15mm.glb`**:
  - *Visual Guidance*: `intake_stock.glb` = narrow curved pipe flange; `intake_reed_15mm.glb` = wider rectangular **reed-valve block housing** (`BOLT_INTAKE_1..2`, inspired by Honda PA50 & Derbi Variant *Flat Reed* blocks) with 15mm+ spigot.
- **`filter_stock_box.glb` vs. `filter_pod_mesh.glb`**:
  - *Visual Guidance*: `filter_stock_box.glb` = molded matte black rectangular/round clamshell airbox; `filter_pod_mesh.glb` = exposed conical pleated red/chrome wire-mesh pod (looks fast, vulnerable to wet coastal fog mist!).
- **`tank_stock.glb`**:
  - *Visual Guidance*: Sculpted steel teardrop/saddle tank (or top-tank capsule shell for `Targa`/`Magnum` style) with filler cap boss on top and bottom petcock boss (`SOCKET_PETCOCK`).

### C. Exhaust Systems (`Models/Parts/Exhaust/`)
- **`pipe_stock_baffled.glb`**:
  - *Real Inspiration*: Stock 22mm cigar/pea-shooter exhaust.
  - *Visual Guidance*: Slim constant-diameter header flowing into a straight cylindrical chrome/black muffler canister with rear baffle endcap screw.
  - *Homage Naming*: `"OEM Quiet Cigar Muffler"`
- **`pipe_circuit_e50_proma.glb`**:
  - *Real Inspiration*: **Proma Circuit** / **Estoril** tight-loop Italian mid-range expansion chambers (`~6,000–8,500 RPM` powerband hit).
  - *Visual Guidance*: Tight S-curved header looping forward/underneath into a **compact multi-cone expansion belly**, turning back into a thin stinger tube and bright aluminum/chrome cylindrical silencer canister (`SOCKET_EXHAUST_BRACKET` tab on belly/stinger).
  - *Homage Naming*: `"Bay Circuit Snake Pipe"` — *"Sluggish off the stoplight, then hits like a slingshot at 22 mph with a signature metallic canister rattle."*
- **`pipe_topend_expansion.glb`**:
  - *Real Inspiration*: **Homoet P4** / **Biturbo** / **MLM** hand-welded straight-cone chambers (`~5,500–9,500+ RPM`).
  - *Visual Guidance*: Long sweeping/straight segmented cones (distinct faceted cone-to-cone weld seams along the body) ending in a tapered stinger and bolt-on black/alloy race silencer.
  - *Homage Naming*: `"P4 Hand-Cone Top-End Chamber"` — *"Segmented raw-steel cones built to scream past 9,000 RPM on the Bay Crossing straight."*

### D. Drivetrain & Chassis Accessories (`Models/Parts/Drivetrain/` & `Models/Parts/Chassis/`)
- **`clutch_e50_stock.glb` / `clutch_e50_stiff.glb`**: Round 3-shoe centrifugal clutch bell with radial spring pockets (paint spring accents **blue** on `clutch_e50_stiff.glb` to evoke classic blue high-RPM launch springs!).
- **`variator_pa50_stock.glb`**: Tapered pulley sheaves + roller ramp cover + V-belt profile (`16x13mm` weight rollers concept).
- **`gearbox_a35_2spd.glb`**: Twin-drum 2-speed clutch basket + spur gears (cork pad / sprag bearing lore in flavor text).
- **Wheels (`wheel_16_spoke.glb` vs. `wheel_16_mag.glb`)**:
  - `wheel_16_spoke.glb`: Slim chrome steel rim + crossed spoke pattern + central 90mm drum hub (`BOLT_AXLE_L/R`, `SOCKET_SPROCKET_MOUNT`).
  - `wheel_16_mag.glb`: **5-star "snowflake" Grimeca-style cast alloy mag** (iconic on Puch Magnum II, Garelli SSXL LTD, and Derbi Variant SLE).
- **Handlebars (`bars_stock_upright.glb`, `bars_cafe_clipon.glb`, `bars_bmx_riser.glb`)**:
  - `bars_stock_upright.glb`: Swept-back commuter rise (~0.14 m rise).
  - `bars_cafe_clipon.glb`: **M-bars / Ace clubman drop** sweeping forward & down near triple clamp (`SOCKET_GRIP_L/R` lowered for aero tuck).
  - `bars_bmx_riser.glb`: Tall **welded BMX crossbar risers** (~0.20 m rise + horizontal brace bar, inspired by *Dirt Hobbit* & *Derbi Variant TT*).
- **Seats (`seat_solo_spring.glb`, `seat_long_buddy.glb`)**:
  - `seat_solo_spring.glb`: **Denfeld-style** pear-shaped solo saddle with twin exposed rear coil springs.
  - `seat_long_buddy.glb`: Long padded **two-up banana/bench buddy seat** extending back toward `SOCKET_CARGO_RACK`.
- **Headlights (`light_round_6v.glb`, `light_boxy_led_12v.glb`)**:
  - `light_round_6v.glb`: Classic **CEV-style round chrome bucket** (~0.13 m lens diameter) with top speedometer recess.
  - `light_boxy_led_12v.glb`: **80s/90s rectangular Euro bezel** headlight (~0.15 × 0.09 m) with optional protective mesh-grid detail.
- **Racks (`rack_rear_wire.glb`, `rack_milk_crate.glb`)**:
  - `rack_rear_wire.glb`: Tubular chrome/steel rear luggage rack with hinged spring-loaded cargo trap.
  - `rack_milk_crate.glb`: Rear wire base topped with a **square perforated HDPE milk crate** (`~0.33 × 0.33 × 0.28 m`) secured by diagonal corner zip-tie/bungee lugs—instantly recognizable Bay Area moped courier signature!

---

## 4. Recommended Future Homage Models (Post-v1 / M6+ Expansion)

Grounded directly in the non-v1 makes observed on `pedalcodearmy.com/stable`, these offer distinct mechanical/visual silhouettes for future content drops:

1. **`Variant TT / SL` (Homage to Derbi Variant TT / SL '79 / '86)**
   - *Visual Silhouette*: Broad stamped sheet-metal monocoque step-through frame; optional TT scrambler spec with braced high bars, knobby tires, and boxy/grilled light.
   - *Mechanical Hook*: **Flat-reed crankcase + variator** ("Start" engine architecture) — bridges `Bramble PA50` CVT tuning with robust step-through cargo frame.
2. **`Relli SSXL` (Homage to Garelli SSXL LTD)**
   - *Visual Silhouette*: Crisp Italian tubular steel **top-tank sport moped** with knee-recess tank, gold/black 5-star alloy mags, and solo cafe saddle.
   - *Mechanical Hook*: High-compression vertical/canted 49cc sport cylinder with short wheelbase nimbleness.
3. **`Ciao Mono` (Homage to Piaggio Ciao)**
   - *Visual Silhouette*: Ultra-slim pressed-steel bicycle-like monocoque spine (tank inside spine), **trailing-link front suspension**, exposed rear pulley channel.
   - *Mechanical Hook*: Featherweight `~40 kg` frame; fastest *Pedal of Shame* cruise speed and lowest rolling drag in the Bay.
4. **`Berg 434` (Homage to Zündapp Bergsteiger M50 Typ 434)**
   - *Visual Silhouette*: Heavy German pressed-shell backbone with high-capacity `~5.3 L` tank, deep valanced fenders, and 90mm drum hubs.
   - *Mechanical Hook*: High-torque hill-climbing gearing suited for `Zone_OaklandHills` switchbacks.
5. **`Hobby X-10` (Homage to KTM Hobby / Puch X-10s)**
   - *Visual Silhouette*: European 1980s stamped full-suspension commuter with angular side shrouds (great canvas for **3D-printed custom side covers**!).

---

## 5. Sources Observed

1. **Pedal Code Army — The Stable & Crew Site**: `https://www.pedalcodearmy.com/stable` · `https://pedalcodearmy.com` · `https://pedalcodearmy.vercel.app` (Observed via `search_web` summaries of fleet roster, owners, Weekly Wobbles events, and 3D-printed Puch Magnum side cover shop).
2. **Puch Maxi & Magnum Specs**: `https://www.myronsmopeds.com` (7/8/9-fin cylinders, Bing 1/12 carbs, Maxi N vs S), `https://en.wikipedia.org/wiki/Puch_Maxi` (1.10–1.12 m wheelbase, 3.2 L tank), `https://www.puchshop.de` (Bing 15mm, Airsal/Homoet/3-shoe clutch tuning), `https://bringatrailer.com` & `https://mopedrich.com` (Puch Magnum II 7 L top-tank, 17" Grimeca 5-star mags, rear rack), `https://www.abikingworld.com` (44.1" wheelbase, 100 mm ground clearance).
3. **Honda Hobbit PA50 / Camino Specs**: `https://en.wikipedia.org/wiki/Honda_PA50` (Honda Benelux origin, PA50I vs PA50II), `https://www.webbikeworld.com` (1,064 mm wheelbase, 1,670 mm length, 2.25-17 wheels, V-matic variator), `https://www.doscycles.com` (reed-valve intake, 16x13mm variator rollers, Polini 46mm, Athena 45mm, Proma Circuit).
4. **Tomos A35 (Targa / Sprint) Specs**: `https://www.mopeddivision.com` (Dellorto SHA 14/12 #53 jet, 16x2.25 tires, cork clutch shoes), `https://www.myronsmopeds.com` (A35 reed intake, 12V/50W ignition), `https://www.icenicam.org.uk` (Tomos 16" 2-speed auto), `https://www.tomoshop.nl` (2-speed cork/sprag clutch).
5. **Derbi Variant, Garelli SSXL, Piaggio Ciao, KTM Hobby, Zündapp Bergsteiger, Puch X-10**: `https://www.myronsmopeds.com` (Derbi Variant SL/TT/RD50 weights, 2.50-17 tires, Flat Reed evolution), `https://www.mopedmotel.com` (Derbi induction generations), `https://www.puch-avello.com` & `https://en.wikipedia.org/wiki/Puch` (Puch X-10s Avello heritage).
6. **Aftermarket Parts & Wheel Sizing**: `https://www.mopeddivision.com` (CEV round vs boxy rectangular lights, Biturbo 28mm pipe), `https://www.dennycycles.com` & `https://www.jmpbparts.com` (Denfeld solo seat, buddy seat, Gilardoni 74cc), `https://www.mitas-moto.com` & `https://www.bikeman.jp` (16x2.25 OD ~530–540 mm; 17x2.25 OD ~556–565 mm).
