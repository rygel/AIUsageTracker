#!/bin/bash
# Validate HTML and JavaScript before building

echo "Validating HTML files..."

# Check for common HTML errors
for file in src/*.html; do
    echo "Checking $file..."
    
    # Check for unclosed tags
    if grep -n '<div[^>]*>[^<]*$' "$file" | grep -v '</div>'; then
        echo "WARNING: Possible unclosed <div> tags in $file"
    fi
    
    # Check for unclosed script tags
    if grep -n '<script' "$file" | grep -v '</script>'; then
        echo "WARNING: Possible unclosed <script> tags in $file"
    fi
done

echo ""
echo "Validation complete!"
