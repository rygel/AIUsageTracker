#!/bin/bash
# Pre-commit validation script
# Run this before committing to ensure code quality

set -e

echo "🔍 Running pre-commit validation..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo ""
echo "📦 Step 1: Building solution..."
dotnet build AIUsageTracker.sln --configuration Release --verbosity minimal
if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Build failed!${NC}"
    exit 1
fi
echo -e "${GREEN}✅ Build successful${NC}"

echo ""
echo "🧪 Step 2: Running tests..."
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release --no-build --verbosity minimal
if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Tests failed!${NC}"
    exit 1
fi
echo -e "${GREEN}✅ Tests passed${NC}"

echo ""
echo "🎨 Step 3: Checking code formatting..."
dotnet format --verify-no-changes --severity warn
if [ $? -ne 0 ]; then
    echo -e "${YELLOW}⚠️  Code formatting issues found${NC}"
    echo "Run 'dotnet format' to fix automatically"
    # Don't exit - just warn
else
    echo -e "${GREEN}✅ Code formatting OK${NC}"
fi

echo ""
echo -e "${GREEN}🎉 All validation checks passed!${NC}"
echo "Ready to commit."
