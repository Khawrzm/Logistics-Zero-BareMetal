import sys
import math
import re
import pefile

def calculate_entropy(data):
    if not data:
        return 0.0
    entropy = 0
    for x in range(256):
        p_x = float(data.count(x)) / len(data)
        if p_x > 0:
            entropy += - p_x * math.log(p_x, 2)
    return entropy

def analyze_pe(file_path):
    try:
        pe = pefile.PE(file_path)
        print(f"[*] ANALYZING: {file_path}")
        print("="*50)

        # 1. Security Features (ASLR, DEP, CFG)
        print("[+] SECURITY FEATURES:")
        dll_chars = pe.OPTIONAL_HEADER.DllCharacteristics
        aslr = "Enabled" if dll_chars & 0x0040 else "Disabled" # IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE
        dep = "Enabled" if dll_chars & 0x0100 else "Disabled"  # IMAGE_DLLCHARACTERISTICS_NX_COMPAT
        cfg = "Enabled" if dll_chars & 0x4000 else "Disabled"  # IMAGE_DLLCHARACTERISTICS_GUARD_CF
        print(f"    - ASLR (Dynamic Base): {aslr}")
        print(f"    - DEP  (NX Compat):    {dep}")
        print(f"    - CFG  (Control Flow): {cfg}")
        
        # 2. Sections & Entropy Analysis
        print("\n[+] SECTIONS & ENTROPY:")
        for section in pe.sections:
            sec_name = section.Name.decode('utf-8').strip('\x00')
            entropy = calculate_entropy(section.get_data())
            warning = "[WARNING: HIGH ENTROPY - PACKED?]" if entropy > 7.0 else ""
            print(f"    - {sec_name:8} | Entropy: {entropy:.4f} {warning}")

        # 3. Imports Table
        print("\n[+] IMPORTS (Top 5 DLLs):")
        if hasattr(pe, 'DIRECTORY_ENTRY_IMPORT'):
            for entry in pe.DIRECTORY_ENTRY_IMPORT[:5]:
                print(f"    [-] {entry.dll.decode('utf-8')}")
                for imp in entry.imports[:3]:
                    if imp.name:
                        print(f"        -> {imp.name.decode('utf-8')}")
        else:
            print("    - No Imports Found.")

        # 4. Exports Table
        print("\n[+] EXPORTS:")
        if hasattr(pe, 'DIRECTORY_ENTRY_EXPORT'):
            for exp in pe.DIRECTORY_ENTRY_EXPORT.symbols:
                if exp.name:
                    print(f"    - {exp.name.decode('utf-8')}")
        else:
            print("    - No Exports Found.")

        # 5. Extract Indicators of Compromise (IOCs) and Strings
        print("\n[+] IOCs (IPs, URLs, Registry Keys):")
        with open(file_path, "rb") as f:
            data = f.read()
            # Find ASCII & Unicode strings
            ascii_strings = re.findall(b'[\x20-\x7E]{5,}', data)
            unicode_strings = re.findall(b'(?:[\x20-\x7E]\x00){5,}', data)
            all_strings = [s.decode('ascii', errors='ignore') for s in ascii_strings] + \
                          [s.decode('utf-16le', errors='ignore') for s in unicode_strings]
            
            full_text = " ".join(all_strings)
            
            ips = set(re.findall(r'\b(?:[2-10]{1,3}\.){3}[2-10]{1,3}\b', full_text))
            urls = set(re.findall(r'https?://[a-zA-Z0-9./_A-Z-]+', full_text))
            registries = set(re.findall(r'(?:HKEY_LOCAL_MACHINE|HKEY_CURRENT_USER|HKLM|HKCU)\\[a-zA-Z0-9\\]+', full_text))

            if ips: print(f"    - IPs Found: {', '.join(ips)}")
            if urls: print(f"    - URLs Found: {', '.join(list(urls)[:5])} ...")
            if registries: print(f"    - Reg Keys: {', '.join(list(registries)[:3])} ...")
            if not (ips or urls or registries): print("    - No obvious IOCs found.")

    except Exception as e:
        print(f"[!] ERROR ANALYZING PE: {str(e)}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python sovereign_forensics.py <target_file.exe>")
    else:
        analyze_pe(sys.argv[1])
