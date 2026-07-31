# 📊 ESAPI Plan Quality Metrics

## 📖 Overview
The **ESAPI Plan Quality Metrics** tool is an open-source automation script developed for the Varian Eclipse Treatment Planning System (TPS), focused on stereotactic (SRS/SBRT) plan QA — including **multi-lesion SRS plans with several PTVs** (e.g., single-isocenter multiple brain metastases).

Evaluating conformity, gradient falloff, and homogeneity manually involves reading Dose-Volume Histograms (DVH) and cross-checking several published index formulas by hand — and for plans with multiple lesions, isolating each lesion's own dose spillage from its neighbors. This manual process is time-consuming and prone to transcription errors. This script automates the extraction and calculation of these dosimetric indices directly from the Eclipse calculation engine, providing a fast, objective, and standardized QA report for every target in the active plan.

## ✨ Key Features
* **Automated DVH Analysis:** Instantly extracts dose and volume metrics directly from the Eclipse calculation engine, eliminating manual curve interpolation.
* **Per-Lesion Metrics for Multi-Target SRS:** Iterates over every PTV in the structure set (not just one target) and reports its own indices, using its own prescription dose when a matching Reference Point overrides it (SIB plans with different dose levels per lesion).
* **Locally-Isolated Indices (ring technique):** For each PTV, two pairs of temporary margin-expanded "ring" structures are generated to compute conformity/gradient indices from the dose local to that lesion, rather than the whole body — which would otherwise mix in dose spillage from neighboring metastases. If the two margins in a pair disagree, the script assumes **dose bridging** between nearby lesions and reports that index as "N/D" instead of a misleading number. (Technique adapted from [Kiragroh/ESAPI_SRS-MultiMets-localMetrics](https://github.com/Kiragroh/ESAPI_SRS-MultiMets-localMetrics).)
* **Standard Stereotactic Indices per PTV:**
  * **Conformity Index — Paddick (CI, local):** `(TV_PIV)² / (TV × PIV_local)`.
  * **Conformity Index — RTOG / PITV (local):** `PIV_local / TV`.
  * **Gradient Index (GI, local):** `V50%_local / V100%_local`.
  * **Homogeneity Index (HI, ICRU 83):** `(D2% − D98%) / D50%` of the PTV.
  * **V12Gy (local):** normal-tissue volume around the lesion receiving ≥12 Gy, a well-known radionecrosis-risk predictor for single-fraction SRS.
  * **Isocenter distance:** distance from the lesion's center to the nearest treatment isocenter — relevant for single-isocenter multi-target delivery accuracy.
* **Plan-Level Metrics:** Rx dose, number of fractions, and **MUR (Monitor Unit Ratio)** — total delivered MU per cGy of dose per fraction.
* **Organized QA Report:** Results are grouped by section — a "Plan" section plus one section per PTV — in the on-screen report, with a matching layout when copied to the clipboard for Excel.
* **Clinical Efficiency:** Reduces plan evaluation time from several minutes of manual inspection to a few seconds of computational processing.

## 💻 System Requirements
* **Eclipse TPS:** Built and tested against ESAPI v18.0 (Varian RTM 18.0). To target a different Eclipse/ESAPI version, update the `HintPath` references in `RadiocirugiaQA.csproj` to point to your clinic's `VMS.TPS.Common.Model.API.dll` / `VMS.TPS.Common.Model.Types.dll`.
* **.NET Framework:** 4.8 (see `TargetFrameworkVersion` in the `.csproj`); adjust to match your clinic's ESAPI version if needed.
* **Write-enabled plugin:** the script creates temporary "ring" and dummy structures per PTV to compute local metrics, and removes them before finishing (`try`/`finally`). It is marked `[assembly: ESAPIScript(IsWriteable = true)]` and calls `context.Patient.BeginModifications()`. When compiled in Eclipse's Script Runner, the resulting `.dll` **must be approved for write access** by an authorized user before it can run on a live structure set — check your clinic's script-governance policy.

## 🛠️ Installation & Compilation
Depending on the specific UI components used in this project, it is highly recommended to compile it into a `.dll` library rather than running it as a standalone `.cs` file.

1. Clone or download this repository to your local machine.
2. Open the solution file (`.sln`) using **Visual Studio**.
3. In `RadiocirugiaQA.csproj`, confirm the `HintPath` of the `VMS.TPS.Common.Model.API` / `VMS.TPS.Common.Model.Types` references points to your clinic's installed ESAPI DLLs; adjust the path if your Eclipse/Varian RTM version or install location differs.
4. Build the solution (`Ctrl + Shift + B` or `Build > Build Solution`).
5. Locate the compiled `.dll` file inside the `bin\Debug` or `bin\Release` folder.
6. In Eclipse, open the Script Runner, navigate to the folder containing your compiled `.dll`, and execute it.

## 🚀 How to Use
1. Open a Patient and calculate a Treatment Plan in Eclipse.
2. Ensure the plan is selected as the active context and has a structure set with at least one **PTV**-type structure. Plans with several PTVs (multi-lesion SRS) are supported natively — each one gets its own section in the report.
3. Run the compiled Plan Quality Metrics `.dll` (write access must be approved in Eclipse the first time, since it creates temporary helper structures).
4. Review the QA report, grouped by PTV, on your screen, and optionally click **Copy to Clipboard** to paste the results into Excel for documentation.
5. The temporary ring/dummy structures used for local metrics (`zPTVring_*`, `zDummyLocal`) are removed automatically before the report is shown; they should never remain in the structure set afterward.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ⚠️ Clinical Disclaimer
**For Research and Educational Purposes Only.** This software is provided "as is", without warranty of any kind. It is the sole responsibility of the clinical user (Medical Physicist, Dosimetrist, or Radiation Oncologist) to strictly verify and validate all extracted metrics and dosimetric data against the native TPS DVH before making any clinical decisions or approving a patient's treatment plan. Because this script writes temporary structures to the patient's structure set, verify that no helper structures (`zPTVring_*`, `zDummyLocal`) remain after execution before approving the structure set.
