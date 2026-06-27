import zipfile
import xml.etree.ElementTree as ET
import os

def read_docx(file_path):
    if not os.path.exists(file_path):
        print(f"File not found: {file_path}")
        return
    
    try:
        with zipfile.ZipFile(file_path) as docx:
            # Check the contents of the zip
            xml_content = docx.read('word/document.xml')
            root = ET.fromstring(xml_content)
            
            # Namespace map for OOXML
            ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
            
            text_runs = []
            for paragraph in root.iter('{http://schemas.openxmlformats.org/wordprocessingml/2006/main}p'):
                para_text = []
                for run in paragraph.iter('{http://schemas.openxmlformats.org/wordprocessingml/2006/main}t'):
                    if run.text:
                        para_text.append(run.text)
                text_runs.append("".join(para_text))
            
            return "\n".join(text_runs)
    except Exception as e:
        return f"Error reading docx: {e}"

if __name__ == "__main__":
    docx_path = r"d:\hoctap\ki8\PRN232\AI-Assisted-Adaptive-English-Learning-System\Huy_Adaptive_Learning_Frontend_Integration_Guide.docx"
    text = read_docx(docx_path)
    
    out_path = r"d:\hoctap\ki8\PRN232\AI-Assisted-Adaptive-English-Learning-System\scratch\docx_content.txt"
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(text)
    print(f"Successfully wrote content to {out_path}")
