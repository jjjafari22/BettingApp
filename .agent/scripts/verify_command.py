import sys
import json
import re

def main():
    try:
        # Read payload from stdin
        payload_str = sys.stdin.read()
        if not payload_str.strip():
            print(json.dumps({"decision": "allow"}))
            return
            
        payload = json.loads(payload_str)
        
        # We only care about run_command
        tool_call = payload.get("toolCall", {})
        if tool_call.get("name") != "run_command":
            print(json.dumps({"decision": "allow"}))
            return
            
        args = tool_call.get("args", {})
        cmd = args.get("CommandLine", "")
        
        # Rule 1: NEVER use dotnet run
        if "dotnet run" in cmd:
            print(json.dumps({
                "decision": "deny",
                "reason": "VIOLATION OF AGENTS.MD: 'dotnet run' is strictly banned as it crashes the machine."
            }))
            return
            
        # Rule 2: NEVER use curl or python to fetch URLs
        if re.search(r'\b(curl|python|python3)\b.*https?://', cmd):
            print(json.dumps({
                "decision": "deny",
                "reason": "VIOLATION OF AGENTS.MD: Using curl or python to fetch URLs is banned. Use read_url_content."
            }))
            return
            
        # Rule 3: NEVER use cat to edit files
        if re.search(r'cat\s+<<.*?EOF.*?>', cmd, re.DOTALL):
            print(json.dumps({
                "decision": "deny",
                "reason": "VIOLATION OF AGENTS.MD: Using 'cat' to create/edit files is banned. Use write_to_file or replace_file_content tools."
            }))
            return
            
        # If no violations, silently allow without asking
        print(json.dumps({
            "decision": "allow"
        }))
        
    except Exception as e:
        print(json.dumps({"decision": "allow"}))

if __name__ == "__main__":
    main()
