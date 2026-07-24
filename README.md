# 📊 ESAPI Plan Quality Metrics

## 📖 Overview
The **ESAPI Plan Quality Metrics** tool is an open-source automation script developed for the Varian Eclipse Treatment Planning System (TPS), focused on stereotactic (SRS/SBRT) plan QA.

Evaluating conformity, gradient falloff, and homogeneity manually involves reading Dose-Volume Histograms (DVH) and cross-checking several published index formulas by hand. This manual process is time-consuming and prone to transcription errors. This script automates the extraction and calculation of these dosimetric indices directly from the Eclipse calculation engine, providing a fast, objective, and standardized QA report for any active plan.

## ✨ Key Features
* **Automated DVH Analysis:** Instantly extracts dose and volume metrics directly from the Eclipse calculation engine, eliminating manual curve interpolation.
* **Standard Stereotactic Indices:** Calculates the metrics most commonly required for SRS/SBRT plan QA:
  * **Conformity Index — Paddick (CI):** `(TV_PIV)² / (TV × PIV)`.
  * **Conformity Index — RTOG / PITV:** `PIV / TV`.
  * **Gradient Index (GI):** `V50% / V100%` of the body.
  * **Homogeneity Index (HI, ICRU 83):** `(D2% − D98%) / D50%` of the PTV.
  * **MUR (Monitor Unit Ratio):** total delivered MU per cGy of dose per fraction.
  * Supporting raw values: Rx dose, PTV volume, V100%/V50% (body), PTV coverage, and D2%/D50%/D98%/Dmax.
* **Organized QA Report:** Results are grouped into clear sections (Prescription, Volumes & Coverage, PTV Dose, Plan Quality Indices, Delivery Complexity) in the on-screen report, with a matching layout when copied to the clipboard for Excel.
* **Clinical Efficiency:** Reduces plan evaluation time from several minutes of manual inspection to a few seconds of computational processing.

## 💻 System Requirements
* **Eclipse TPS:** Built and tested against ESAPI v18.0 (Varian RTM 18.0). To target a different Eclipse/ESAPI version, update the `HintPath` references in `RadiocirugiaQA.csproj` to point to your clinic's `VMS.TPS.Common.Model.API.dll` / `VMS.TPS.Common.Model.Types.dll`.
* **.NET Framework:** 4.8 (see `TargetFrameworkVersion` in the `.csproj`); adjust to match your clinic's ESAPI version if needed.

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
2. Ensure the plan is selected as the active context and has a structure set with a **PTV** (or a designated Target Volume) and a **BODY/External** structure.
3. Run the compiled Plan Quality Metrics `.dll`.
4. Review the grouped QA report on your screen, and optionally click **Copy to Clipboard** to paste the results into Excel for documentation.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ⚠️ Clinical Disclaimer
**For Research and Educational Purposes Only.** This software is provided "as is", without warranty of any kind. It is the sole responsibility of the clinical user (Medical Physicist, Dosimetrist, or Radiation Oncologist) to strictly verify and validate all extracted metrics and dosimetric data against the native TPS DVH before making any clinical decisions or approving a patient's treatment plan.
